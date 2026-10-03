import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { Tone } from './status';

@Component({
  selector: 'app-status-pill',
  template: `<span class="pill" [attr.data-tone]="tone()"
    ><span class="dot" aria-hidden="true"></span>{{ label() }}</span
  >`,
  styles: `
    :host {
      display: inline-flex;
    }
    [data-tone='ok'] .dot {
      background: var(--color-primary);
    }
    [data-tone='busy'] .dot {
      background: var(--color-accent-yellow);
      box-shadow: 0 0 0 1px #c9a800 inset;
    }
    [data-tone='bad'] .dot {
      background: var(--color-accent-tomato);
    }
    [data-tone='bad'] {
      color: var(--color-danger-ink);
    }
    [data-tone='gone'] {
      color: var(--color-ink-mute);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPill {
  readonly label = input.required<string>();
  readonly tone = input<Tone>('idle');
}
