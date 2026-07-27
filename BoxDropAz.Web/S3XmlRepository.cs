using System.Xml.Linq;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace BoxDropAz.Web;

/// <summary>
/// Persists DataProtection keys to S3 so auth cookies survive Lambda cold starts and
/// stay valid across concurrent execution environments.
/// </summary>
public sealed class S3XmlRepository : IXmlRepository
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly string _keyPrefix;

    public S3XmlRepository(IAmazonS3 s3, string bucketName, string keyPrefix)
    {
        _s3 = s3;
        _bucketName = bucketName;
        _keyPrefix = keyPrefix.TrimEnd('/');
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        if (string.IsNullOrWhiteSpace(_bucketName))
        {
            return Array.Empty<XElement>();
        }

        try
        {
            return GetAllElementsAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: unable to read DataProtection keys from S3: {ex.Message}");
            return Array.Empty<XElement>();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(_bucketName))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("N") : friendlyName;

        try
        {
            _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = $"{_keyPrefix}/{name}.xml",
                ContentBody = element.ToString(SaveOptions.DisableFormatting)
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: unable to store DataProtection key in S3: {ex.Message}");
        }
    }

    private async Task<IReadOnlyCollection<XElement>> GetAllElementsAsync()
    {
        var elements = new List<XElement>();
        string? continuationToken = null;

        do
        {
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = $"{_keyPrefix}/",
                ContinuationToken = continuationToken
            });

            foreach (var obj in response.S3Objects ?? [])
            {
                if (!obj.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var getResponse = await _s3.GetObjectAsync(_bucketName, obj.Key);
                using var reader = new StreamReader(getResponse.ResponseStream);
                var xml = await reader.ReadToEndAsync();
                elements.Add(XElement.Parse(xml));
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken != null);

        return elements;
    }
}
