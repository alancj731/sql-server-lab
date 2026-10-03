import { Injectable, inject, signal } from '@angular/core';
import { LabsApi, problemMessage } from '../api/labs-api';
import type { JobDto, LabDto } from '../api/models';
import { isActive } from '../shared/status';

/** Per-lab state shared by the lab shell and its tabs. Always reflects backend state. */
@Injectable()
export class LabContext {
  private readonly api = inject(LabsApi);

  readonly labId = signal<string | null>(null);
  readonly lab = signal<LabDto | null>(null);
  readonly jobs = signal<JobDto[]>([]);
  readonly error = signal<string | null>(null);
  readonly notFound = signal(false);

  load(labId: string): void {
    this.labId.set(labId);
    this.lab.set(null);
    this.jobs.set([]);
    this.notFound.set(false);
    this.refresh();
  }

  refresh(): void {
    const id = this.labId();
    if (!id) return;
    this.api.get(id).subscribe({
      next: (lab) => {
        this.lab.set(lab);
        this.error.set(null);
      },
      error: (err) => {
        this.notFound.set(err?.status === 404);
        this.error.set(problemMessage(err));
      },
    });
    this.api.jobs(id).subscribe({ next: (jobs) => this.jobs.set(jobs), error: () => undefined });
  }

  applyLab(lab: LabDto): void {
    if (lab.id === this.labId()) {
      this.lab.set(lab);
    }
  }

  applyJob(job: JobDto): void {
    if (job.labId !== this.labId()) return;
    this.jobs.update((jobs) =>
      jobs.some((j) => j.jobId === job.jobId)
        ? jobs.map((j) => (j.jobId === job.jobId ? job : j))
        : [job, ...jobs],
    );
    this.lab.update((lab) => {
      if (!lab) return lab;
      if (isActive(job)) return { ...lab, currentJob: job };
      return lab.currentJob?.jobId === job.jobId ? { ...lab, currentJob: null } : lab;
    });
  }
}
