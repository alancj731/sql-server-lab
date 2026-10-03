import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Observable } from 'rxjs';
import { LabsApi, problemMessage } from '../api/labs-api';
import type { JobDto, LabCommand } from '../api/models';
import { Clock } from '../core/clock';
import { LabEvents } from '../core/lab-events';
import { JobProgress } from '../shared/job-progress';
import { StatusPill } from '../shared/state-pill';
import {
  can,
  dateTime,
  isActive,
  jobTypeLabel,
  powerLabel,
  relativeTime,
  stateTone,
} from '../shared/status';
import { DeleteLabDialog, DeleteLabDialogData } from './delete-lab-dialog';
import { LabContext } from './lab-context';

@Component({
  selector: 'app-lab-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, StatusPill, JobProgress],
  providers: [LabContext],
  template: `
    <nav class="caption crumbs" aria-label="Breadcrumb">
      <a routerLink="/labs">Labs</a> / {{ lab()?.name ?? '…' }}
    </nav>

    @if (ctx.notFound()) {
      <section class="card stack">
        <h1 class="heading-lg">Lab not found</h1>
        <p class="mute">It may have been deleted, or it belongs to someone else.</p>
        <a routerLink="/labs" class="btn btn-secondary">Back to labs</a>
      </section>
    } @else if (lab(); as lab) {
      <header class="header card">
        <div class="title-row">
          <div class="stack-sm">
            <div class="row">
              <h1 class="display-md">{{ lab.name }}</h1>
              <app-status-pill [label]="lab.state" [tone]="tone()" />
              @if (lab.isSimulated) {
                <span class="pill">Simulated</span>
              }
            </div>
            <p class="caption">
              <span class="mono">{{ lab.region }}</span> · VM {{ power(lab) }} · expires
              <span [title]="date(lab.expiresAt)">{{ relative(lab.expiresAt) }}</span>
            </p>
            @if (lab.stateReason) {
              <p class="caption reason" [class.bad]="lab.state === 'Failed'">
                {{ lab.stateReason }}
              </p>
            }
          </div>

          <div class="actions" role="group" aria-label="Lab actions">
            <button
              class="btn btn-primary"
              type="button"
              [disabled]="!enabled('Start')"
              (click)="run('Start')"
            >
              Start
            </button>
            <button
              class="btn btn-secondary"
              type="button"
              [disabled]="!enabled('Deallocate')"
              (click)="run('Deallocate')"
            >
              Deallocate
            </button>
            <button
              class="btn btn-secondary"
              type="button"
              [disabled]="!enabled('ExtendExpiration')"
              (click)="extend()"
            >
              Extend 2h
            </button>
            <button
              class="btn btn-danger"
              type="button"
              [disabled]="!enabled('Delete')"
              (click)="confirmDelete()"
            >
              Delete…
            </button>
          </div>
        </div>

        @if (currentJob(); as job) {
          <div class="current">
            <app-job-progress [job]="job" />
            @if (!job.cancelRequested) {
              <button
                class="btn btn-link caption"
                type="button"
                [disabled]="busy()"
                (click)="cancel(job)"
              >
                Cancel job
              </button>
            }
          </div>
        }

        @if (actionError()) {
          <div class="alert alert-danger" role="alert">{{ actionError() }}</div>
        }
      </header>

      <nav class="tabs" aria-label="Lab sections">
        @for (tab of tabs; track tab.path) {
          <a [routerLink]="tab.path" routerLinkActive="active" ariaCurrentWhenActive="page">{{
            tab.label
          }}</a>
        }
      </nav>

      <router-outlet />
    } @else if (ctx.error()) {
      <div class="alert alert-danger" role="alert">
        {{ ctx.error() }}
        <button class="btn btn-link" type="button" (click)="ctx.refresh()">Retry</button>
      </div>
    } @else {
      <p class="mute" role="status">Loading lab…</p>
    }
  `,
  styles: `
    .crumbs {
      margin-bottom: var(--space-lg);
    }
    .header {
      display: flex;
      flex-direction: column;
      gap: var(--space-lg);
      margin-bottom: var(--space-xl);
    }
    .title-row {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: var(--space-lg);
    }
    .stack-sm {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
      min-width: 0;
    }
    .stack-sm p {
      margin: 0;
    }
    h1 {
      overflow-wrap: anywhere;
    }
    .reason.bad {
      color: var(--color-danger-ink);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      align-items: flex-start;
    }
    .current {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
      align-items: flex-start;
      padding-top: var(--space-lg);
      border-top: 1px solid var(--color-hairline-cool);
    }
    .current app-job-progress {
      width: 100%;
      max-width: 560px;
    }
    .tabs {
      display: flex;
      gap: var(--space-xs);
      overflow-x: auto;
      border-bottom: 1px solid var(--color-hairline-cool);
      margin-bottom: var(--space-xl);
    }
    .tabs a {
      padding: var(--space-sm) var(--space-md);
      text-decoration: none;
      color: var(--color-ink-mute);
      font-size: 14px;
      font-weight: 500;
      border-bottom: 2px solid transparent;
      white-space: nowrap;
      min-height: 36px;
      display: inline-flex;
      align-items: center;
    }
    .tabs a:hover {
      color: var(--color-ink);
    }
    .tabs a.active {
      color: var(--color-ink);
      border-bottom-color: var(--color-ink);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabShell implements OnInit, OnDestroy {
  readonly labId = input.required<string>();

  protected readonly ctx = inject(LabContext);
  private readonly api = inject(LabsApi);
  private readonly events = inject(LabEvents);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly clock = inject(Clock);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly lab = this.ctx.lab;
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly currentJob = computed(() => {
    const job = this.lab()?.currentJob;
    return job && isActive(job) ? job : null;
  });
  protected readonly tone = computed(() => stateTone(this.lab()?.state ?? 'Requested'));
  protected readonly date = dateTime;
  protected readonly power = powerLabel;
  protected readonly tabs = [
    { path: 'overview', label: 'Overview' },
    { path: 'index-lab', label: 'Index lab' },
    { path: 'deadlock-lab', label: 'Deadlock lab' },
    { path: 'backups', label: 'Backups' },
    { path: 'maintenance', label: 'Maintenance' },
    { path: 'audit', label: 'Audit' },
  ];

  private subscribed: string | null = null;

  constructor() {
    effect(() => {
      const id = this.labId();
      if (this.subscribed && this.subscribed !== id) {
        void this.events.unsubscribeLab(this.subscribed);
      }

      this.subscribed = id;
      this.ctx.load(id);
      void this.events.subscribeLab(id);
    });
  }

  ngOnInit(): void {
    this.events.labChanged
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((lab) => this.ctx.applyLab(lab));
    this.events.jobChanged
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((job) => this.ctx.applyJob(job));
    this.events.reconnected
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.ctx.refresh());
  }

  ngOnDestroy(): void {
    if (this.subscribed) {
      void this.events.unsubscribeLab(this.subscribed);
    }
  }

  protected relative(iso: string): string {
    return relativeTime(iso, this.clock.now());
  }

  /** A command is enabled only if the backend allows it, no mutating job is active, and no request is in flight. */
  protected enabled(command: LabCommand): boolean {
    const lab = this.lab();
    if (!lab || this.busy() || !can(lab, command)) return false;
    return command === 'ExtendExpiration' || !this.currentJob();
  }

  protected run(command: 'Start' | 'Deallocate'): void {
    const id = this.labId();
    this.track(command === 'Start' ? this.api.start(id) : this.api.deallocate(id), (job) =>
      this.announce(job),
    );
  }

  protected extend(): void {
    this.track(this.api.extendExpiration(this.labId(), 2), (lab) => {
      this.ctx.applyLab(lab);
      this.snackBar.open(`Expiry extended to ${dateTime(lab.expiresAt)}`, undefined, {
        duration: 4000,
      });
    });
  }

  protected cancel(job: JobDto): void {
    this.track(this.api.cancelJob(job.jobId), (updated) => {
      this.ctx.applyJob(updated);
      this.snackBar.open('Cancellation requested', undefined, { duration: 4000 });
    });
  }

  protected confirmDelete(): void {
    const lab = this.lab();
    if (!lab) return;
    this.dialog
      .open<DeleteLabDialog, DeleteLabDialogData, JobDto>(DeleteLabDialog, {
        data: { labId: lab.id, labName: lab.name },
        width: 'min(480px, calc(100vw - 32px))',
        autoFocus: '#confirm-name',
      })
      .afterClosed()
      .subscribe((job) => {
        if (job) {
          this.announce(job);
        }
      });
  }

  private announce(job: JobDto): void {
    this.ctx.applyJob(job);
    this.ctx.refresh();
    this.snackBar.open(`${jobTypeLabel(job.type)} requested`, undefined, { duration: 4000 });
  }

  private track<T>(request: Observable<T>, onSuccess: (value: T) => void): void {
    this.busy.set(true);
    this.actionError.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        onSuccess(value);
      },
      error: (err) => {
        this.busy.set(false);
        this.actionError.set(problemMessage(err));
        this.ctx.refresh();
      },
    });
  }
}
