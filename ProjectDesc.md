# Project Specification: Moving Crate Rental & Realtor Gifting Portal
**Target Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, AWS Lambda, Amazon API Gateway, Amazon DynamoDB, Tailwind CSS (Frontend Client & Portal)

---

## 1. Project Overview
A serverless web application built for a local moving crate rental business. The platform fulfills two primary functions:
1. **Direct-to-Consumer (D2C) E-commerce:** Allows homeowners and renters to select moving crate bundles (e.g., 2-bedroom or 4-bedroom packages), choose delivery/pickup dates, and check out securely via Stripe.
2. **B2B Realtor Gifting Portal:** Allows local real estate agents to maintain a monthly subscription, track available closing gift "credits," and submit a clean form to gift moving crates to their homebuyer clients (with automated co-branding inserts).
3. **Client Upsell Mechanism:** When a homebuyer receives the realtor's gift package, they can log in via a unique link to upgrade their crate count, applying the realtor's gifted dollar value as a direct discount toward a larger bundle.
4. Their credit card will need to be kept on file inc ase of damages to equipment.
5. Add a rental terms of services, make sure that the end user accepts it as part of the checkout process which describes prices if damages occur, $40 per replacement crate or whatever is reasonable.  Start with the ability to extend past the basic period for a weekly fee depending on the package they choose.  Look at competitors for pricing.
---

## 2. Core User Roles & Workflows

### A. The Homebuyer / Direct Renter
* **Landing Page & Checkout:** Clean, modern mobile-friendly interface showcasing package tiers (e.g., Medium 50-crate bundle vs. Large 100-crate bundle).
* **Date Picker & Logistics:** Select delivery address and schedule drop-off and pickup windows in Maricopa, Casa Grande, or surrounding Pinal/Maricopa county zones.
* **Gift Claim & Upsell:** If arriving via a Realtor's gift link, the system auto-applies a credit balance (e.g., $100 value) and prompts the user to pay the difference if they want to upgrade their inventory.

### B. The Realtor Subscriber
* **Agent Dashboard:** A secure login portal displaying current subscription status, monthly credit allocation, and rolling credit balances.
* We need an agent landing page which explains the benefits of using this program.  Gifting their clients assistance after their move.
* **Gift Order Submission:** Form inputs for Client Name, Property Address, and Closing Date. Deducts credits instantly upon submission and triggers an automated fulfillment task for the operator.

---
## C. Worker
Logs in and sees orders that need to be delivered or are ready for pickup with addresses and dates and time.

## D. Regional Admin
The site will be brokend down into regions. Example, Phoenix / Tuscon
Sees all menus.
Can impersonate any user, in their region.
Has revenue graphs for their region
Can do user management, delete / update orders, for their region
All orders should have notes fields where the admin can update

## D. SaaS Admin
Sees all menus.
Can impersonate any user.
Has region management

## 3. Technical Architecture & Database Design 
This should follow the architecture and design methodology of C:\Users\ianwa\source\repos\StatementToExcel

