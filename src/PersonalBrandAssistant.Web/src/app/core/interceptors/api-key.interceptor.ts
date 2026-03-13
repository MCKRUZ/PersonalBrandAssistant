import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../environments/environment';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  if (!environment.apiKey || !req.url.startsWith(environment.apiUrl)) {
    return next(req);
  }

  const cloned = req.clone({
    setHeaders: { 'X-Api-Key': environment.apiKey },
  });

  return next(cloned);
};
