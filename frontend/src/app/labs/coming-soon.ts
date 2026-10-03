import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Honest placeholder for features delivered by a later milestone. */
@Component({
  selector: 'app-coming-soon',
  template: `
    <section class="card stack">
      <span class="pill">Milestone {{ milestone() }}</span>
      <h2 class="heading-lg">{{ heading() }}</h2>
      <p class="mute">{{ summary() }}</p>
      <p class="caption">Not available in this build. No action on this page changes your lab.</p>
    </section>
  `,
  styles: `
    .pill {
      align-self: flex-start;
    }
    p {
      margin: 0;
      max-width: 70ch;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComingSoon {
  readonly heading = input('');
  readonly milestone = input(0);
  readonly summary = input('');
}
