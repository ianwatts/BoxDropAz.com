using System.Net;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Inline HTML email bodies. Email clients strip external stylesheets, so everything here is
/// inlined and table free enough to survive Gmail and Outlook.
/// </summary>
public static class EmailTemplates
{
    public static string Wrap(string heading, string bodyHtml, string? ctaText = null, string? ctaUrl = null)
    {
        var cta = string.Empty;
        if (!string.IsNullOrWhiteSpace(ctaText) && !string.IsNullOrWhiteSpace(ctaUrl))
        {
            cta = $"""
                <p style="margin:32px 0 8px;">
                  <a href="{WebUtility.HtmlEncode(ctaUrl)}" style="background:#0f766e;color:#ffffff;text-decoration:none;padding:14px 28px;border-radius:8px;font-weight:600;display:inline-block;">{WebUtility.HtmlEncode(ctaText)}</a>
                </p>
                <p style="font-size:12px;color:#6b7280;margin-top:16px;">If the button does not work, paste this into your browser:<br /><span style="color:#0f766e;">{WebUtility.HtmlEncode(ctaUrl)}</span></p>
                """;
        }

        return $"""
            <!DOCTYPE html>
            <html>
            <body style="margin:0;padding:0;background:#f3f4f6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <div style="max-width:600px;margin:0 auto;padding:24px;">
                <div style="background:#0f766e;border-radius:12px 12px 0 0;padding:20px 28px;">
                  <span style="color:#ffffff;font-size:20px;font-weight:700;letter-spacing:-0.5px;">BoxDrop AZ</span>
                  <span style="color:#99f6e4;font-size:13px;margin-left:10px;">Reusable moving totes</span>
                </div>
                <div style="background:#ffffff;border-radius:0 0 12px 12px;padding:32px 28px;color:#111827;font-size:15px;line-height:1.6;">
                  <h1 style="margin:0 0 16px;font-size:22px;color:#111827;">{WebUtility.HtmlEncode(heading)}</h1>
                  {bodyHtml}
                  {cta}
                </div>
                <p style="text-align:center;color:#9ca3af;font-size:12px;margin-top:20px;">
                  BoxDrop AZ &middot; Serving the East Valley, Casa Grande and Pinal County<br />
                  Questions? Just reply to this email.
                </p>
              </div>
            </body>
            </html>
            """;
    }

    /// <summary>Renders a label/value summary block used by order and gift emails.</summary>
    public static string DetailRows(params (string Label, string Value)[] rows)
    {
        var cells = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => $"""
                <tr>
                  <td style="padding:6px 0;color:#6b7280;font-size:14px;">{WebUtility.HtmlEncode(r.Label)}</td>
                  <td style="padding:6px 0;color:#111827;font-size:14px;font-weight:600;text-align:right;">{WebUtility.HtmlEncode(r.Value)}</td>
                </tr>
                """);

        return $"""
            <table style="width:100%;border-collapse:collapse;margin:20px 0;border-top:1px solid #e5e7eb;border-bottom:1px solid #e5e7eb;">
              {string.Concat(cells)}
            </table>
            """;
    }
}
