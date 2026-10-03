import type { JobDto, LabDto } from '../api/models';
import { can, isActive, jobStatusLabel, jobTypeLabel, relativeTime, stateTone } from './status';

describe('status helpers', () => {
  it('maps backend job status to user-facing labels without optimism', () => {
    expect(jobStatusLabel('Queued')).toBe('requested');
    expect(jobStatusLabel('Running')).toBe('running');
    expect(jobStatusLabel('Succeeded')).toBe('succeeded');
    expect(jobStatusLabel('Failed')).toBe('failed');
    expect(jobStatusLabel('Cancelled')).toBe('cancelled');
  });

  it('classifies lab states', () => {
    expect(stateTone('Ready')).toBe('ok');
    expect(stateTone('Stopped')).toBe('idle');
    expect(stateTone('Provisioning')).toBe('busy');
    expect(stateTone('Deleting')).toBe('busy');
    expect(stateTone('Failed')).toBe('bad');
    expect(stateTone('Deleted')).toBe('gone');
  });

  it('formats relative times', () => {
    const now = Date.parse('2026-01-01T12:00:00Z');
    expect(relativeTime('2026-01-01T13:52:00Z', now)).toBe('in 1h 52m');
    expect(relativeTime('2026-01-01T11:57:00Z', now)).toBe('3m ago');
    expect(relativeTime('2026-01-01T12:00:10Z', now)).toBe('now');
  });

  it('only allows commands the backend lists', () => {
    const lab = { allowedActions: ['Deallocate', 'ExtendExpiration'] } as unknown as LabDto;
    expect(can(lab, 'Deallocate')).toBe(true);
    expect(can(lab, 'Start')).toBe(false);
    expect(can(null, 'Start')).toBe(false);
  });

  it('detects active jobs and labels job types', () => {
    expect(isActive({ status: 'Queued' } as JobDto)).toBe(true);
    expect(isActive({ status: 'Running' } as JobDto)).toBe(true);
    expect(isActive({ status: 'Failed' } as JobDto)).toBe(false);
    expect(isActive(null)).toBe(false);
    expect(jobTypeLabel('DeallocateVm')).toBe('Deallocate VM');
    expect(jobTypeLabel('RunIndexBenchmark')).toBe('Run Index Benchmark');
  });
});
