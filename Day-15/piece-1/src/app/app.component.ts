// Root standalone component — no NgModule, no declarations array.
// Day-14 piece-2 is the SIGNAL FORMS rebuild, so the screen hosts
// QuoteFormSignalsComponent (not the reactive QuoteFormComponent, whose file
// is kept in the repo — but not imported here — for the side-by-side
// comparison in the README). Both share the singleton QuotesService, so a
// successful create reloads the list.

import { Component }                  from '@angular/core';
import { AuthBarComponent }           from './auth-bar/auth-bar.component';
import { QuoteFormSignalsComponent }  from './quote-form-signals/quote-form-signals.component';
import { QuotesListComponent }        from './quotes-list/quotes-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [AuthBarComponent, QuoteFormSignalsComponent, QuotesListComponent],
  template: `
    <app-auth-bar />
    <app-quote-form-signals />
    <app-quotes-list />
  `,
})
export class AppComponent {}
