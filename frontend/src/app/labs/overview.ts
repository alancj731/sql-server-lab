import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { StatusPill } from '../shared/state-pill';
import {
  dateTime,
  jobStatusLabel,
  jobStatusTone,
  jobTypeLabel,
  powerLabel,
} from '../shared/status';
import { LabContext } from './lab-context';

@Component({
  selector: 'app-overview',
  imports: [StatusPill],
  template: `
    @if (ctx.lab(); as lab) {
      <div class="grid">
        <section class="card card-tight" aria-labelledby="details-h">
          <h2 id="details-h" class="heading-md">Details</h2>
          <dl class="details">
            <div>
              <dt>Resource group</dt>
              <dd class="mono">{{ lab.resourceGroupName }}</dd>
            </div>
            <div>
              <dt>Region</dt>
              <dd class="mono">{{ lab.region }}</dd>
            </div>
            <div>
              <dt>Owner</dt>
              <dd>{{ lab.ownerName }}</dd>
            </div>
            <div>
              <dt>VM power</dt>
              <dd>{{ power(lab) }}</dd>
            </div>
            <div>
              <dt>Created</dt>
              <dd>{{ date(lab.createdAt) }}</dd>
            </div>
            <div>
              <dt>Expires</dt>
              <dd>{{ date(lab.expiresAt) }}</dd>
            </div>
            <div>
              <dt>Last change</dt>
              <dd>{{ date(lab.updatedAt) }}</dd>
            </div>
            <div>
              <dt>Mode</dt>
              <dd>{{ lab.isSimulated ? 'Simulated (local)' : 'Azure' }}</dd>
            </div>
          </dl>
        </section>

        <section class="card card-tight dark" aria-labelledby="next-h">
          <h2 id="next-h" class="heading-md">What you can do</h2>
          <ul>
            <li><strong>Deallocate</strong> when idle — compute stops billing, disks remain.</li>
            <li>
              <strong>Start</strong> to bring SQL Server back; the lab is Ready once SQL answers
              health checks.
            </li>
            <li><strong>Extend</strong> up to a 24-hour total lifetime.</li>
            <li><strong>Delete</strong> removes the entire lab resource group.</li>
          </ul>
        </section>
      </div>

      <section class="history" aria-labelledby="history-h">
        <h2 id="history-h" class="heading-md">Operation history</h2>
        @if (ctx.jobs().length === 0) {
          <p class="mute">No operations yet.</p>
        } @else {
          <div class="table-wrap">
            <table class="table">
              <caption class="visually-hidden">
                Operations for this lab, newest first
              </caption>
              <thead>
                <tr>
                  <th scope="col">Operation</th>
                  <th scope="col">Status</th>
                  <th scope="col">Progress</th>
                  <th scope="col">Requested</th>
                  <th scope="col">By</th>
                  <th scope="col">Completed</th>
                  <th scope="col">Details</th>
                </tr>
              </thead>
              <tbody>
                @for (job of ctx.jobs(); track job.jobId) {
                  <tr>
                    <td>{{ typeLabel(job.type) }}</td>
                    <td>
                      <app-status-pill
                        [label]="statusLabel(job.status)"
                        [tone]="statusTone(job.status)"
                      />
                    </td>
                    <td>{{ job.progress }}%</td>
                    <td>{{ date(job.requestedAt) }}</td>
                    <td>{{ job.requestedBy }}</td>
                    <td>{{ date(job.completedAt) }}</td>
                    <td class="caption">
                      @if (job.errorMessage) {
                        <span [class.bad]="job.status === 'Failed'"
                          >{{ job.errorCategory }}: {{ job.errorMessage }}</span
                        >
                      } @else {
                        {{ job.currentStep ?? '' }}
                      }
                      @if (job.attemptCount > 1) {
                        · {{ job.attemptCount }} attempts
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    }
  `,
  styles: `
    .grid {
      display: grid;
      gap: var(--space-lg);
      grid-template-columns: minmax(0, 1fr);
      margin-bottom: var(--space-xxl);
    }
    @media (min-width: 1024px) {
      .grid {
        grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);
      }
    }
    .details {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
      gap: var(--space-md) var(--space-xl);
      margin: var(--space-lg) 0 0;
    }
    dt {
      font-size: 13px;
      color: var(--color-ink-mute);
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .dark {
      background: var(--color-canvas-night);
      color: var(--color-on-dark);
      border-color: var(--color-canvas-night);
    }
    .dark ul {
      margin: var(--space-md) 0 0;
      padding-left: 18px;
      font-size: 14px;
      display: flex;
      flex-direction: column;
      gap: var(--space-sm);
    }
    .history h2 {
      margin-bottom: var(--space-md);
    }
    .bad {
      color: var(--color-danger-ink);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Overview {
  protected readonly ctx = inject(LabContext);
  protected readonly date = dateTime;
  protected readonly power = powerLabel;
  protected readonly typeLabel = jobTypeLabel;
  protected readonly statusLabel = jobStatusLabel;
  protected readonly statusTone = jobStatusTone;
}
