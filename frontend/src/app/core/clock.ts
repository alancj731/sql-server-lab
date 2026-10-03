import { Injectable, signal } from '@angular/core';

/** Shared ticking "now" so relative times stay current without per-component timers. */
@Injectable({ providedIn: 'root' })
export class Clock {
  readonly now = signal(Date.now());

  constructor() {
    setInterval(() => this.now.set(Date.now()), 15_000);
  }
}
