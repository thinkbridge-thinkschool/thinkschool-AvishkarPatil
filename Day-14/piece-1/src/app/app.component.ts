// Root standalone component — no NgModule, no declarations array.
// AppComponent hosts the create-a-quote form (Day-14) above the
// list+detail screen (carried over from Day-13). They share the singleton
// QuotesService, so a successful create reloads the list automatically.

import { Component }            from '@angular/core';
import { AuthBarComponent }     from './auth-bar/auth-bar.component';
import { QuoteFormComponent }   from './quote-form/quote-form.component';
import { QuotesListComponent }  from './quotes-list/quotes-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [AuthBarComponent, QuoteFormComponent, QuotesListComponent],
  template: `
    <app-auth-bar />
    <app-quote-form />
    <app-quotes-list />
  `,
})
export class AppComponent {}
