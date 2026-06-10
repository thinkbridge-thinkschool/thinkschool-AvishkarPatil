import { Injectable, effect, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _dark = signal<boolean>(localStorage.getItem('theme') === 'dark');
  readonly dark = this._dark.asReadonly();

  constructor() {
    document.body.classList.toggle('dark', this._dark());
    effect(() => {
      const isDark = this._dark();
      document.body.classList.toggle('dark', isDark);
      localStorage.setItem('theme', isDark ? 'dark' : 'light');
    });
  }

  toggle(): void { this._dark.update(d => !d); }
}
