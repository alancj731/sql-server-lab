import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import type { JobDto } from '../api/models';
import { jobStatusLabel, jobTypeLabel } from './status';

/** Durable job progress from backend state; announced politely to assistive technology. */
@Component({
  selector: 'app-job-progress',
  imports: [MatProgressBarModule],
  template: `
    @let j = job();
    <div class="job" role="status" aria-live="polite">
      <div class="line">
        <span class="type">{{ typeLabel() }}</span>
        <span class="status">{{ statusLabel() }} · {{ j.progress }}%</span>
      </div>
      <mat-progress-bar
        [mode]="j.status === 'Queued' ? 'buffer' : 'determinate'"
        [value]="j.progress"
        [attr.aria-label]="typeLabel() + ' progress'"
      />
      @if (!compact()) {
        <div class="step caption">
          {{ j.currentStep ?? 'Waiting for worker' }}
          @if (j.attemptCount > 1) {
            · attempt {{ j.attemptCount }} of {{ j.maxAttempts }}
          }
          @if (j.cancelRequested) {
            · cancellation requested
          }
        </div>
      }
    </div>
  `,
  styles: `
    .job {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 160px;
    }
    .line {
      display: flex;
      justify-content: space-between;
      gap: 8px;
      font-size: 13px;
    }
    .type {
      font-weight: 500;
    }
    .status {
      color: var(--color-ink-mute);
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobProgress {
  readonly job = input.required<JobDto>();
  readonly compact = input(false);
  protected readonly typeLabel = computed(() => jobTypeLabel(this.job().type));
  protected readonly statusLabel = computed(() => jobStatusLabel(this.job().status));
}
