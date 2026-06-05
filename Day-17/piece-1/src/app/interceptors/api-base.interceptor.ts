import { HttpInterceptorFn } from '@angular/common/http';
import { inject, InjectionToken } from '@angular/core';

// Day-17: in production the SPA is served from Azure Static Web Apps and the
// API lives on a DIFFERENT origin (the Azure Container App). There is no dev
// proxy in production, so relative `/api/...` URLs must be rewritten to the
// absolute deployed API base. This interceptor does that rewrite centrally —
// every service keeps using clean relative `/api/...` URLs.
//
// API_BASE_URL defaults to '' (same-origin / dev proxy). app.config.ts provides
// the deployed Container App URL for the build that ships to SWA.
//
// ORDER: this runs AFTER authInterceptor, so authInterceptor still matches the
// relative `/api` prefix to attach the Bearer token before the URL is rewritten.
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  factory: () => '',
});

export const apiBaseInterceptor: HttpInterceptorFn = (req, next) => {
  const base = inject(API_BASE_URL);
  if (base && req.url.startsWith('/api')) {
    req = req.clone({ url: `${base}${req.url}` });
  }
  return next(req);
};
