import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { LabsApi } from '../api/labs-api';
import { DeleteLabDialog } from './delete-lab-dialog';

describe('DeleteLabDialog', () => {
  const close = vi.fn();
  const del = vi.fn(() => of({ jobId: 'job-1' }));

  beforeEach(async () => {
    close.mockClear();
    del.mockClear();
    await TestBed.configureTestingModule({
      imports: [DeleteLabDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { labId: 'lab-1', labName: 'perf-lab' } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: LabsApi, useValue: { delete: del } },
      ],
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(DeleteLabDialog);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const input = el.querySelector<HTMLInputElement>('#confirm-name')!;
    const button = [...el.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Delete lab'),
    )!;
    const type = async (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, button, type };
  }

  it('stays disabled until the exact lab name is typed', async () => {
    const { button, type } = setup();
    expect(button.disabled).toBe(true);
    await type('PERF-LAB');
    expect(button.disabled).toBe(true);
    await type('perf-lab');
    expect(button.disabled).toBe(false);
  });

  it('calls the API with the typed name and closes with the job', async () => {
    const { button, type } = setup();
    await type('perf-lab');
    button.click();
    expect(del).toHaveBeenCalledWith('lab-1', 'perf-lab');
    expect(close).toHaveBeenCalledWith({ jobId: 'job-1' });
  });
});
