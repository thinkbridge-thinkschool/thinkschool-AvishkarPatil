// Angular dev-server proxy (vite under the hood).
//
// Rewrites /api → http://localhost:5075 so the browser makes a same-origin
// call and no CORS config is needed on the API.
//
// WHY this is a .js file and not .json:
// When the API is stopped, the proxy target refuses the connection
// (ECONNREFUSED).  By default vite logs the error but leaves the browser's
// request socket OPEN — the XHR never settles, so the Angular app's
// httpResource stays in its loading state forever and the error branch never
// renders.
//
// The `configure` hook below attaches an 'error' handler to the underlying
// http-proxy instance.  When the target is unreachable it writes an immediate
// 502 JSON response, so the browser receives a real HTTP error.  httpResource
// then transitions to its error state (isLoading() → false, error() set) and
// the component's `@else if (error())` branch renders right away.
module.exports = {
  '/api': {
    target: 'http://localhost:5075',
    secure: false,
    changeOrigin: true,
    configure: (proxy) => {
      proxy.on('error', (err, _req, res) => {
        // res is the http.ServerResponse for the browser request.
        // Guard against double-writes (vite may also react to the error).
        if (res && typeof res.writeHead === 'function' && !res.headersSent) {
          res.writeHead(502, { 'Content-Type': 'application/json' });
        }
        if (res && typeof res.end === 'function') {
          res.end(
            JSON.stringify({
              error: 'API unreachable',
              detail: String(err && err.message ? err.message : err),
            }),
          );
        }
      });
    },
  },
};
