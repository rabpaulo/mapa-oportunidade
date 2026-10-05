import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  request<T>(path: string, body?: unknown) {
    const response = body === undefined ? this.http.get<T>('/api/' + path) : this.http.post<T>('/api/' + path, body);
    return response.pipe(catchError((error: HttpErrorResponse) => throwError(() => new Error(
      typeof error.error?.detail === 'string' ? error.error.detail : error.status === 429
        ? 'Muitas perguntas em pouco tempo. Aguarde um minuto e tente novamente.'
        : 'Não foi possível concluir a operação. Confira sua conexão e tente novamente.'
    ))));
  }
}
