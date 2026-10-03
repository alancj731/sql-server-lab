import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LabsApi, problemMessage } from '../api/labs-api';
import type { JobDto, LabDto } from '../api/models';
import { Clock } from '../core/clock';
import { LabEvents } from '../core/lab-events';
import { JobProgress } from '../shared/job-progress';
import { StatusPill } from '../shared/state-pill';
import { dateTime, isActive, powerLabel, relativeTime, stateTone } from '../shared/status';

@Component({
  selector: 'app-lab-list',
  imports: [RouterLink, StatusPill, JobProgress],
  template: `
    <div class="head">
      <div>
        <h1 class="display-md">Labs</h1>
        <p class="mute">Disposable SQL Server Developer VMs for performance experiments.</p>
      </div>
    </div>

    @if (error()) {
      <div class="alert alert-danger" role="alert">
        {{ error() }} <button class="btn-link btn" type="button" (click)="load()">Retry</button>
      </div>
    }

    @if (loading() && labs().length === 0) {
      <p class="mute" role="status">Loading labs…</p>
    } @else if (labs().length === 0 && !error()) {
      <section class="card empty">
        <h2 class="heading-lg">No labs yet</h2>
        <p class="mute">
          Create a lab to provision a SQL Server VM. It expires automatically, so abandoned labs
          never run indefinitely.
        </p>
        <a routerLink="/labs/new" class="btn btn-primary">Create your first lab</a>
      </section>
    } @else {
      <div class="table-wrap desktop">
        <table class="table">
          <caption class="visually-hidden">
            Your labs
          </caption>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">State</th>
              <th scope="col">Region</th>
              <th scope="col">Owner</th>
              <th scope="col">Expires</th>
              <th scope="col">VM power</th>
              <th scope="col">Current job</th>
            </tr>
          </thead>
          <tbody>
            @for (lab of labs(); track lab.id) {
              <tr>
                <td>
                  <a [routerLink]="['/labs', lab.id, 'overview']" class="name">{{ lab.name }}</a>
                  @if (lab.isSimulated) {
                    <span class="caption"> · simulated</span>
                  }
                </td>
                <td><app-status-pill [label]="lab.state" [tone]="tone(lab)" /></td>
                <td class="mono">{{ lab.region }}</td>
                <td>{{ lab.ownerName }}</td>
                <td [title]="date(lab.expiresAt)">{{ relative(lab.expiresAt) }}</td>
                <td>{{ power(lab) }}</td>
                <td class="job-cell">
                  @if (active(lab.currentJob)) {
                    <app-job-progress [job]="lab.currentJob!" [compact]="true" />
                  } @else {
                    <span class="mute">—</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <ul class="cards mobile" aria-label="Your labs">
        @for (lab of labs(); track lab.id) {
          <li class="card card-tight">
            <div class="row between">
              <a [routerLink]="['/labs', lab.id, 'overview']" class="name">{{ lab.name }}</a>
              <app-status-pill [label]="lab.state" [tone]="tone(lab)" />
            </div>
            <dl class="meta caption">
              <div>
                <dt>Region</dt>
                <dd class="mono">{{ lab.region }}</dd>
              </div>
              <div>
                <dt>Owner</dt>
                <dd>{{ lab.ownerName }}</dd>
              </div>
              <div>
                <dt>Expires</dt>
                <dd>{{ relative(lab.expiresAt) }}</dd>
              </div>
              <div>
                <dt>VM power</dt>
                <dd>{{ power(lab) }}</dd>
              </div>
            </dl>
            @if (active(lab.currentJob)) {
              <app-job-progress [job]="lab.currentJob!" [compact]="true" />
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .head {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: var(--space-lg);
      margin-bottom: var(--space-xl);
    }
    .head p {
      margin: var(--space-xs) 0 0;
    }
    .name {
      font-weight: 500;
      text-decoration: none;
    }
    .name:hover {
      text-decoration: underline;
    }
    .job-cell {
      min-width: 200px;
    }
    .empty {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-md);
    }
    .empty p {
      margin: 0;
      max-width: 56ch;
    }
    .cards {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-md);
    }
    .between {
      justify-content: space-between;
    }
    .meta {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: var(--space-sm);
      margin: var(--space-md) 0;
    }
    .meta dt {
      color: var(--color-ink-mute-2);
    }
    .meta dd {
      margin: 0;
      color: var(--color-ink);
    }
    .mobile {
      display: flex;
    }
    .desktop {
      display: none;
    }
    @media (min-width: 900px) {
      .mobile {
        display: none;
      }
      .desktop {
        display: block;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabList implements OnInit {
  private readonly api = inject(LabsApi);
  private readonly events = inject(LabEvents);
  private readonly clock = inject(Clock);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly labs = signal<LabDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly tone = (lab: LabDto) => stateTone(lab.state);
  protected readonly power = powerLabel;
  protected readonly active = isActive;
  protected readonly date = dateTime;
  protected relative(iso: string): string {
    return relativeTime(iso, this.clock.now());
  }

  ngOnInit(): void {
    this.load();
    this.events.labChanged
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((lab) => this.applyLab(lab));
    this.events.jobChanged
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((job) => this.applyJob(job));
    this.events.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (labs) => {
        this.labs.set(labs);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(problemMessage(err));
        this.loading.set(false);
      },
    });
  }

  private applyLab(lab: LabDto): void {
    this.labs.update((labs) => {
      if (lab.state === 'Deleted') {
        return labs.filter((l) => l.id !== lab.id);
      }

      const index = labs.findIndex((l) => l.id === lab.id);
      return index >= 0 ? labs.map((l) => (l.id === lab.id ? lab : l)) : [lab, ...labs];
    });
  }

  private applyJob(job: JobDto): void {
    this.labs.update((labs) =>
      labs.map((lab) => {
        if (lab.id !== job.labId) return lab;
        if (isActive(job)) return { ...lab, currentJob: job };
        return lab.currentJob?.jobId === job.jobId ? { ...lab, currentJob: null } : lab;
      }),
    );
  }
}
