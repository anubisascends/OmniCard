# OmniCard eBay token broker

A tiny Cloudflare Worker that lets every OmniCard install connect to eBay **with no eBay setup of its
own**. It holds the eBay keyset's secret (`CertId`), so the secret never ships inside OmniCard.

```
Browser ──Connect──▶ eBay sign-in ──▶ broker /callback ──302──▶ that server's /api/ebay/callback
OmniCard server ──POST /token (code or refresh token)──▶ broker ──+ CertId──▶ eBay token endpoint
```

- **`POST /token`**: OmniCard sends the sign-in code (or, about every 2 hours, the refresh token). The
  broker adds the keyset's credentials and passes the request on to eBay. Only those two kinds of
  request are allowed.
- **`GET /callback`**: your RuName's accepted URL. OmniCard puts its own callback address in the OAuth
  `state`, so the broker sends each user's browser back to *their* server, even one that's only on a
  home network. If `state` is missing or isn't an OmniCard callback address, the broker shows the
  address so the user can paste it into OmniCard instead.

OmniCard uses the broker when its `eBay:BrokerUrl` is set and `eBay:CertId` is blank. An install with
its own keyset sets `CertId`, which always wins.

## One-time setup

You need a free [Cloudflare account](https://dash.cloudflare.com/sign-up), Node 18+, and your eBay
**Production** keyset (App ID, Cert ID and RuName) from [developer.ebay.com](https://developer.ebay.com).
Every command runs from this folder:

```bash
cd tools/ebay-broker
```

### 1. Sign in to Cloudflare

```bash
npx wrangler login
```

A browser window opens. Allow access, then return to the terminal. `npx` downloads Wrangler, the
Cloudflare CLI, the first time.

### 2. Pick the eBay environment

`wrangler.toml` sets `EBAY_ENVIRONMENT = "production"`. Change it to `"sandbox"` only if you're using a
Sandbox keyset. OmniCard's `eBay:Environment` must match.

### 3. Deploy the Worker

```bash
npx wrangler deploy
```

The first time, Cloudflare asks you to choose a `workers.dev` subdomain. When it finishes, it prints
the Worker's address, for example:

```
https://omnicard-ebay-broker.<your-subdomain>.workers.dev
```

Write it down. This is your **broker URL**.

### 4. Store the eBay keys as Worker secrets

Run each command and paste the value when asked. Values are encrypted by Cloudflare and never appear in
this repo.

```bash
npx wrangler secret put EBAY_APP_ID     # App ID (Client ID)
npx wrangler secret put EBAY_CERT_ID    # Cert ID (Client Secret)
npx wrangler secret put EBAY_RU_NAME    # RuName (eBay Redirect URL name)
```

Check it's alive: opening the broker URL in a browser shows **OmniCard eBay broker is running.**

### 5. Point your eBay RuName at the broker

1. On [developer.ebay.com](https://developer.ebay.com), open **Application Keys**, then **User Tokens**
   for your Production keyset.
2. Under **Get a Token from eBay via Your Application**, edit the RuName you stored in step 4.
3. Make sure **OAuth** is selected (not Auth'n'Auth), then set both:
   - **Your auth accepted URL**: `https://<your broker URL>/callback`
   - **Your auth declined URL**: `https://<your broker URL>/callback`
4. Save.

> eBay requires production keysets to handle **Marketplace Account Deletion** notifications (or to
> claim an exemption) before they work for other sellers. If eBay hasn't already cleared your keyset,
> see **Alerts & Notifications** in the developer portal. OmniCard stores no eBay buyer data, which is
> the basis for the exemption.

### 6. Ship the defaults with OmniCard

In `OmniCard.Web/appsettings.json`, fill the `eBay` section and commit it. None of these values are
secret; `CertId` stays blank:

```json
"eBay": {
  "AppId": "<your App ID>",
  "CertId": "",
  "DevId": "",
  "RuName": "<your RuName>",
  "Environment": "production",
  "BrokerUrl": "https://omnicard-ebay-broker.<your-subdomain>.workers.dev"
}
```

Every release built from then on can connect to eBay out of the box: **Administration ▸ eBay ▸ Connect
to eBay**, sign in, and eBay brings the user straight back, connected.

**Your own servers:** a server whose own config sets `eBay:CertId` keeps calling eBay directly. Clear
`CertId` there, or remove it, to switch that server to the broker. On a development machine, you can
keep a `CertId` in `dotnet user-secrets` to test without the broker, or leave it out to test the broker
flow.

## Running it

- **Update the Worker** after changing `src/index.js`: `npx wrangler deploy`.
- **Watch live requests** for troubleshooting: `npx wrangler tail`. The Worker never logs codes or tokens.
- **Rotate the Cert ID** if it ever leaks: reset it in the eBay developer portal, then run
  `npx wrangler secret put EBAY_CERT_ID`. No OmniCard install needs updating, and connected installs
  stay connected.
- **Free plan limits**: Cloudflare's free plan allows 100,000 requests a day. Each connected install uses
  about 12 a day (one token refresh every 2 hours, and only while it's making eBay calls).

## Security notes

- The `CertId` lives only in Cloudflare's encrypted secrets.
- `/token` is public, but it only accepts a sign-in code or a refresh token, and those can only be
  obtained by an eBay seller agreeing to *your* app on eBay. A caller gets nothing more than that seller
  would get by using OmniCard. Every call does count toward your keyset's eBay API limits. To cap abuse,
  add a Cloudflare **Rate limiting rule** for the path `/token`. One rule is free: **Security ▸ WAF ▸
  Rate limiting rules**, which needs the Worker on a custom domain.
- `/callback` only redirects to `http(s)` addresses whose path ends in `/api/ebay/callback`, so it
  can't be used to send people to arbitrary sites. Each OmniCard server also accepts only sign-ins it
  started itself: a single-use code in `state` that expires after 15 minutes.
