import { Component, inject } from '@angular/core';
import { Toast, ToastService } from './toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  template: `
    <div class="toast-container"
         aria-live="polite"
         aria-atomic="false"
         role="region"
         aria-label="Notifications">
      @for (t of toastSvc.toasts(); track t.id) {
        <div class="toast toast--{{ t.type }}" role="status">
          <span class="toast-icon" aria-hidden="true">{{ icon(t.type) }}</span>
          <span class="toast-msg">{{ t.message }}</span>
          <button class="toast-close"
                  type="button"
                  (click)="toastSvc.dismiss(t.id)"
                  aria-label="Dismiss notification">×</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      bottom: 1.5rem;
      right: 1.5rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      z-index: 9000;
      width: min(360px, calc(100vw - 2rem));
      pointer-events: none;
    }
    .toast {
      display: flex;
      align-items: flex-start;
      gap: 0.6rem;
      padding: 0.7rem 0.875rem;
      border-radius: 8px;
      border-left: 4px solid;
      box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
      font-size: 0.875rem;
      line-height: 1.4;
      pointer-events: auto;
      animation: toast-in 0.22s ease-out;
    }
    @keyframes toast-in {
      from { opacity: 0; transform: translateX(110%); }
      to   { opacity: 1; transform: translateX(0); }
    }
    .toast--success {
      background: var(--toast-success-bg);
      color: var(--toast-success-text);
      border-color: var(--toast-success-border);
    }
    .toast--error {
      background: var(--toast-error-bg);
      color: var(--toast-error-text);
      border-color: var(--toast-error-border);
    }
    .toast--warning {
      background: var(--toast-warning-bg);
      color: var(--toast-warning-text);
      border-color: var(--toast-warning-border);
    }
    .toast-icon { font-size: 0.9rem; flex-shrink: 0; margin-top: 0.05rem; }
    .toast-msg  { flex: 1; word-break: break-word; }
    .toast-close {
      background: none;
      border: none;
      cursor: pointer;
      font-size: 1.15rem;
      color: inherit;
      opacity: 0.55;
      padding: 0;
      line-height: 1;
      flex-shrink: 0;
      transition: opacity 0.15s;
    }
    .toast-close:hover { opacity: 1; }
    .toast-close:focus-visible {
      outline: 2px solid currentColor;
      outline-offset: 2px;
      border-radius: 2px;
    }
    @media (max-width: 480px) {
      .toast-container { left: 1rem; right: 1rem; bottom: 1rem; width: auto; }
    }
  `],
})
export class ToastComponent {
  protected readonly toastSvc = inject(ToastService);

  protected icon(type: Toast['type']): string {
    if (type === 'success') return '✓';
    if (type === 'error')   return '⚠';
    return 'ℹ';
  }
}
