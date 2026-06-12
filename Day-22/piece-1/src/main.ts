// Day-13 piece-1 — Signals + zoneless + standalone
//
// bootstrapApplication is the entry point for a standalone (no NgModule) app.
// The appConfig object carries every provider the app needs — in this case
// provideZonelessChangeDetection() instead of the traditional zone.js import.
//
// Zoneless means Angular no longer monkey-patches browser APIs (setTimeout,
// XHR, Promises) to detect when something might have changed.  Instead it
// only re-renders when a signal is written to.  The change-detection pass is
// therefore always targeted and never speculative.

import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig }            from './app/app.config';
import { AppComponent }         from './app/app.component';

bootstrapApplication(AppComponent, appConfig)
  .catch(err => console.error(err));
