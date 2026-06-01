import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';

// provideZonelessChangeDetection() replaces zone.js entirely.
// Angular no longer needs to be notified via zone patches; instead, every
// signal write marks the consuming view as dirty and schedules a microtask
// to flush pending updates. This makes rendering fully reactive and removes
// the ~100 KB zone.js payload from the production bundle.
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
  ],
};
