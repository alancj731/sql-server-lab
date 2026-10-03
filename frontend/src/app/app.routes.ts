import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'labs' },
  {
    path: 'labs',
    title: 'Labs · SQL Server Lab',
    loadComponent: () => import('./labs/lab-list').then((m) => m.LabList),
  },
  {
    path: 'labs/new',
    title: 'New lab · SQL Server Lab',
    loadComponent: () => import('./labs/new-lab').then((m) => m.NewLab),
  },
  {
    path: 'labs/:labId',
    loadComponent: () => import('./labs/lab-shell').then((m) => m.LabShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        title: 'Overview · SQL Server Lab',
        loadComponent: () => import('./labs/overview').then((m) => m.Overview),
      },
      {
        path: 'index-lab',
        title: 'Index lab · SQL Server Lab',
        loadComponent: () => import('./labs/coming-soon').then((m) => m.ComingSoon),
        data: {
          heading: 'Index experiment',
          milestone: 5,
          summary:
            'Run the same orders query with and without IX_Orders_CustomerId_OrderDate and compare P50/P95 duration, CPU, logical reads, and the scan-versus-seek plan side by side.',
        },
      },
      {
        path: 'deadlock-lab',
        title: 'Deadlock lab · SQL Server Lab',
        loadComponent: () => import('./labs/coming-soon').then((m) => m.ComingSoon),
        data: {
          heading: 'Deadlock experiment',
          milestone: 6,
          summary:
            'Two sessions update accounts in opposite order until SQL Server picks a victim (error 1205). You will see both transaction timelines, the victim, the locked resources, and the consistent-order fix that completes without a deadlock. Blocking is one session waiting on another; a deadlock is a cycle that can only end by killing one of them.',
        },
      },
      {
        path: 'backups',
        title: 'Backups · SQL Server Lab',
        loadComponent: () => import('./labs/coming-soon').then((m) => m.ComingSoon),
        data: {
          heading: 'Backups',
          milestone: 8,
          summary:
            'Full backups to Blob Storage with RESTORE VERIFYONLY and an optional test restore to a temporary database. Credentials never reach the browser.',
        },
      },
      {
        path: 'maintenance',
        title: 'Maintenance · SQL Server Lab',
        loadComponent: () => import('./labs/coming-soon').then((m) => m.ComingSoon),
        data: {
          heading: 'Maintenance and patching',
          milestone: 9,
          summary:
            'Assess available SQL Server and Windows updates through Azure Update Manager, then install approved updates after a recent backup and a typed confirmation.',
        },
      },
      {
        path: 'audit',
        title: 'Audit · SQL Server Lab',
        loadComponent: () => import('./labs/audit').then((m) => m.Audit),
      },
    ],
  },
  { path: '**', redirectTo: 'labs' },
];
