import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { LabsApi, problemMessage } from '../api/labs-api';
import type { AuditEventDto } from '../api/models';
import { dateTime } from '../shared/status';
import { LabContext } from './lab-context';

@Component({
  selector: 'app-audit',
  template: `
    <div class="row between">
      <h2 class="heading-md">Audit log</h2>
      <button class="btn btn-secondary" type="button" (click)="load()">Refresh</button>
    </div>
    <p class="caption">Append-only record of every request and state change for this lab.</p>
    @if (error()) {
      <div class="alert alert-danger" role="alert">{{ error() }}</div>
    } @else if (events().length === 0) {
      <p class="mute" role="status">{{ loaded() ? 'No events.' : 'Loading…' }}</p>
    } @else {
      <div class="table-wrap">
        <table class="table">
          <caption class="visually-hidden">
            Audit events, newest first
          </caption>
          <thead>
            <tr>
              <th scope="col">Time</th>
              <th scope="col">Actor</th>
              <th scope="col">Action</th>
              <th scope="col">Detail</th>
              <th scope="col">Correlation</th>
            </tr>
          </thead>
          <tbody>
            @for (e of events(); track e.id) {
              <tr>
                <td>{{ date(e.occurredAt) }}</td>
                <td>{{ e.actorId }}</td>
                <td class="mono">{{ e.action }}</td>
                <td class="caption">{{ e.detail }}</td>
                <td class="mono caption">{{ e.correlationId.slice(0, 12) }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    .between {
      justify-content: space-between;
    }
    p {
      margin: var(--space-xs) 0 var(--space-lg);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Audit {
  private readonly ctx = inject(LabContext);
  private readonly api = inject(LabsApi);
  protected readonly events = signal<AuditEventDto[]>([]);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly date = dateTime;

  constructor() {
    effect(() => {
      // Reload whenever the lab changes (state transitions add audit events).
      this.ctx.lab();
      this.load();
    });
  }

  load(): void {
    const id = this.ctx.labId();
    if (!id) return;
    this.api.audit(id).subscribe({
      next: (events) => {
        this.events.set(events);
        this.loaded.set(true);
        this.error.set(null);
      },
      error: (err) => this.error.set(problemMessage(err)),
    });
  }
}
