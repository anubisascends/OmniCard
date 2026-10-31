// OmniCard eBay token broker (Cloudflare Worker). Holds the eBay keyset's CertId so OmniCard installs
// never need it, and relays eBay's OAuth redirect back to whichever OmniCard server started the
// sign-in. Setup and the security model are in ../README.md.
//
//   POST /token     OmniCard → eBay token endpoint, with this keyset's credentials added.
//                   Form fields: grant_type=authorization_code&code=…  or  grant_type=refresh_token&refresh_token=…&scope=…
//   GET  /callback  The RuName's accepted (and declined) URL. Redirects the browser to the OmniCard
//                   callback named in `state`; without a usable `state`, shows the address to paste.
//
// Secrets: EBAY_APP_ID, EBAY_CERT_ID, EBAY_RU_NAME. Var: EBAY_ENVIRONMENT (production | sandbox).

const TOKEN_PATH = '/identity/v1/oauth2/token';
const CALLBACK_SUFFIX = '/api/ebay/callback';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === '/token' && request.method === 'POST') return token(request, env);
    if (url.pathname === '/callback' && request.method === 'GET') return callback(url);
    if (url.pathname === '/' && request.method === 'GET') return new Response('OmniCard eBay broker is running.\n');
    return new Response('Not found\n', { status: 404 });
  },
};

function apiBase(env) {
  return env.EBAY_ENVIRONMENT === 'production' ? 'https://api.ebay.com' : 'https://api.sandbox.ebay.com';
}

/** Mirrors eBay's OAuth error shape so OmniCard logs it the same way as a direct call. */
function oauthError(status, error, description) {
  return Response.json(
    { error, error_description: description },
    { status, headers: { 'Cache-Control': 'no-store' } },
  );
}

async function token(request, env) {
  if (!env.EBAY_APP_ID || !env.EBAY_CERT_ID || !env.EBAY_RU_NAME) {
    return oauthError(500, 'server_error', 'The broker is missing its eBay secrets.');
  }

  let form;
  try {
    form = await request.formData();
  } catch {
    return oauthError(400, 'invalid_request', 'Expected a form-encoded body.');
  }

  // Only the two grants OmniCard uses, and only their own fields: the broker never forwards
  // anything else to eBay under this keyset's credentials.
  const grant = form.get('grant_type');
  const body = new URLSearchParams({ grant_type: grant ?? '' });
  if (grant === 'authorization_code') {
    const code = form.get('code');
    if (!code) return oauthError(400, 'invalid_request', 'Missing code.');
    body.set('code', code);
    body.set('redirect_uri', env.EBAY_RU_NAME);
  } else if (grant === 'refresh_token') {
    const refreshToken = form.get('refresh_token');
    if (!refreshToken) return oauthError(400, 'invalid_request', 'Missing refresh_token.');
    body.set('refresh_token', refreshToken);
    const scope = form.get('scope');
    if (scope) body.set('scope', scope);
  } else {
    return oauthError(400, 'unsupported_grant_type', 'Only authorization_code and refresh_token are supported.');
  }

  const res = await fetch(apiBase(env) + TOKEN_PATH, {
    method: 'POST',
    headers: {
      Authorization: 'Basic ' + btoa(`${env.EBAY_APP_ID}:${env.EBAY_CERT_ID}`),
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body,
  });
  return new Response(res.body, {
    status: res.status,
    headers: {
      'Content-Type': res.headers.get('Content-Type') ?? 'application/json',
      'Cache-Control': 'no-store',
    },
  });
}

function callback(url) {
  const params = url.searchParams;
  const state = params.get('state');
  const target = returnUrl(state);
  if (!target) return pastePage(url);

  const code = params.get('code');
  if (code && params.get('isAuthSuccessful') !== 'false') target.searchParams.set('code', code);
  else target.searchParams.set('error', params.get('error') || 'access_denied');
  target.searchParams.set('state', state);
  return Response.redirect(target.toString(), 302);
}

/**
 * OmniCard sends state = base64url(JSON { r: "<its own …/api/ebay/callback>", n: "<nonce>" }). Only
 * http(s) URLs ending in the OmniCard callback path are accepted, so this can't become an open
 * redirect to arbitrary pages; OmniCard checks the nonce itself.
 */
function returnUrl(state) {
  if (!state) return null;
  try {
    const b64 = state.replace(/-/g, '+').replace(/_/g, '/');
    const { r } = JSON.parse(atob(b64 + '==='.slice((b64.length + 3) % 4)));
    const u = new URL(r);
    if (u.protocol !== 'https:' && u.protocol !== 'http:') return null;
    if (u.username || u.password || !u.pathname.endsWith(CALLBACK_SUFFIX)) return null;
    u.search = '';
    u.hash = '';
    return u;
  } catch {
    return null;
  }
}

const escapeHtml = (s) =>
  s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** Fallback when the sign-in didn't come from a recognisable OmniCard server: hand the address back
 * to the user so they can paste it into OmniCard's Connect to eBay window. */
function pastePage(url) {
  const ok = url.searchParams.get('code') && url.searchParams.get('isAuthSuccessful') !== 'false';
  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>OmniCard eBay sign-in</title>
<style>body{font:16px/1.5 system-ui,sans-serif;max-width:640px;margin:40px auto;padding:0 16px}
textarea{width:100%;height:7em;font:13px monospace}</style></head><body>
${
  ok
    ? `<h1>Almost done</h1>
<p>Copy this whole address and paste it into OmniCard: <b>Administration ▸ eBay ▸ Paste a code instead</b>. It expires in about 5 minutes.</p>
<textarea readonly onclick="this.select()">${escapeHtml(url.toString())}</textarea>`
    : `<h1>eBay access wasn't granted</h1><p>Go back to OmniCard and click <b>Connect to eBay</b> to try again.</p>`
}
</body></html>`;
  return new Response(html, {
    headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' },
  });
}
