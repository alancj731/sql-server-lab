import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { LabsApi } from '../api/labs-api';
import type { JobDto, LabDto } from '../api/models';
import { LabContext } from './lab-context';

const lab = (overrides: Partial<LabDto> = {}): LabDto =>
  ({
    id: 'lab-1',
    name: 'a',
    state: 'Ready',
    allowedActions: [],
    currentJob: null,
    ...overrides,
  }) as LabDto;
const job = (overrides: Partial<JobDto> = {}): JobDto =>
  ({
    jobId: 'job-1',
    labId: 'lab-1',
    status: 'Running',
    type: 'DeallocateVm',
    progress: 10,
    ...overrides,
  }) as JobDto;

describe('LabContext', () => {
  let ctx: LabContext;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LabContext,
        { provide: LabsApi, useValue: { get: () => of(lab()), jobs: () => of([]) } },
      ],
    });
    ctx = TestBed.inject(LabContext);
    ctx.load('lab-1');
  });

  it('tracks an active job pushed over SignalR', () => {
    ctx.applyJob(job());
    expect(ctx.lab()?.currentJob?.jobId).toBe('job-1');
    expect(ctx.jobs()).toHaveLength(1);
  });

  it('clears the current job when it finishes and updates history in place', () => {
    ctx.applyJob(job());
    ctx.applyJob(job({ status: 'Succeeded', progress: 100 }));
    expect(ctx.lab()?.currentJob).toBeNull();
    expect(ctx.jobs()).toHaveLength(1);
    expect(ctx.jobs()[0].status).toBe('Succeeded');
  });

  it('ignores events for other labs', () => {
    ctx.applyJob(job({ labId: 'lab-2' }));
    ctx.applyLab(lab({ id: 'lab-2', state: 'Failed' }));
    expect(ctx.jobs()).toHaveLength(0);
    expect(ctx.lab()?.state).toBe('Ready');
  });
});
