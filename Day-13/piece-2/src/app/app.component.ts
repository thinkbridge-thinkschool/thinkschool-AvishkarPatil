// Root standalone component — no NgModule, no declarations array.
// AppComponent hosts the quotes list+detail screen.

import { Component }            from '@angular/core';
import { QuotesListComponent }  from './quotes-list/quotes-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [QuotesListComponent],
  template: `<app-quotes-list />`,
})
export class AppComponent {}
