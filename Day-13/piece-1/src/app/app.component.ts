// Root standalone component — no NgModule, no declarations array.
// AppComponent is the host that imports and renders CollectionViewerComponent.

import { Component }                   from '@angular/core';
import { CollectionViewerComponent }   from './collection-viewer/collection-viewer.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CollectionViewerComponent],
  template: `<app-collection-viewer />`,
})
export class AppComponent {}
