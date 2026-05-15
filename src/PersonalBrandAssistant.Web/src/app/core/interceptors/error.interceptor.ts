import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { catchError, throwError } from 'rxjs';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const messageService = inject(MessageService);

  return next(req).pipe(
    catchError((error) => {
      const status = error.status;
      let detail = 'An unexpected error occurred';

      if (status === 0) {
        detail = 'Unable to reach the server — check your connection';
      } else if (status === 400) {
        detail = error.error?.detail ?? 'Validation error';
      } else if (status === 401) {
        detail = 'API key invalid or missing';
      } else if (status === 404) {
        detail = 'Resource not found';
      } else if (status === 409) {
        detail = 'Conflict — data was modified by another request';
      } else if (status >= 500) {
        detail = 'Server error — please try again';
      }

      messageService.add({
        severity: 'error',
        summary: `Error ${status}`,
        detail,
      });

      return throwError(() => error);
    })
  );
};
