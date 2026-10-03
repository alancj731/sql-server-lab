import type { JobDto, LabCommand, LabDto, LabJobStatus, LabJobType, LabState } from '../api/models';

export type Tone = 'ok' | 'busy' | 'idle' | 'bad' | 'gone';

const TRANSITIONAL: readonly LabState[] = [
  'Requested',
  'Provisioning',
  'Starting',
  'Configuring',
  'Deallocating',
  'Maintenance',
  'Deleting',
];

export function stateTone(state: LabState): Tone {
  if (state === 'Ready') return 'ok';
  if (state === 'Stopped') return 'idle';
  if (state === 'Failed') return 'bad';
  if (state === 'Deleted') return 'gone';
  return TRANSITIONAL.includes(state) ? 'busy' : 'idle';
}

/** Backend job status as shown to users. Never derived optimistically. */
export function jobStatusLabel(status: LabJobStatus): string {
  switch (status) {
    case 'Queued':
      return 'requested';
    case 'Running':
      return 'running';
    case 'Succeeded':
      return 'succeeded';
    case 'Failed':
      return 'failed';
    case 'Cancelled':
      return 'cancelled';
  }
}

export function jobStatusTone(status: LabJobStatus): Tone {
  switch (status) {
    case 'Queued':
    case 'Running':
      return 'busy';
    case 'Succeeded':
      return 'ok';
    case 'Failed':
      return 'bad';
    case 'Cancelled':
      return 'idle';
  }
}

export function isActive(job: JobDto | null | undefined): boolean {
  return !!job && (job.status === 'Queued' || job.status === 'Running');
}

const JOB_LABELS: Partial<Record<LabJobType, string>> = {
  ProvisionLab: 'Provision lab',
  StartVm: 'Start VM',
  DeallocateVm: 'Deallocate VM',
  DeleteLab: 'Delete lab',
  ReconcileLab: 'Reconcile state',
};

export function jobTypeLabel(type: LabJobType): string {
  return JOB_LABELS[type] ?? type.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function can(lab: LabDto | null | undefined, command: LabCommand): boolean {
  return !!lab && lab.allowedActions.includes(command);
}

export function powerLabel(lab: LabDto): string {
  switch (lab.powerState) {
    case 'NotCreated':
      return 'Not created';
    case 'Deallocated':
      return 'Deallocated';
    default:
      return lab.powerState;
  }
}

/** "in 1h 52m", "3m ago", "now". */
export function relativeTime(iso: string, now: number): string {
  const diffMs = new Date(iso).getTime() - now;
  const abs = Math.abs(diffMs);
  const minutes = Math.round(abs / 60_000);
  if (minutes < 1) return 'now';
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  const span = h > 0 ? `${h}h ${m}m` : `${m}m`;
  return diffMs >= 0 ? `in ${span}` : `${span} ago`;
}

export function dateTime(iso: string | null | undefined): string {
  return iso
    ? new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
    : '—';
}
