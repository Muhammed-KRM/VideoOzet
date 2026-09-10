import { HttpInterceptorFn } from '@angular/common/http';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('admin_token');
  
  if (token) {
    const clonedReq = req.clone({
      setHeaders: {
        'X-API-Key': token
      }
    });
    return next(clonedReq);
  }
  
  return next(req);
};
