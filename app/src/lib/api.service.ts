import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Store } from './store';
import { catchError, throwError } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly store = inject(Store);
  private readonly http = inject(HttpClient);
  private params(path: string): Record<string, string> { return ['ufs', 'privacidade', 'saude', 'coleta'].includes(path) ? {} : { uf: this.store.uf() }; }
  download(path: string, body: unknown) { return this.http.post('/api/' + path, body, { params: this.params(path), responseType: 'blob' }); }
  request<T>(path: string, body?: unknown) {
    const response = body === undefined ? this.http.get<T>('/api/' + path, { params: this.params(path) }) : this.http.post<T>('/api/' + path, body, { params: this.params(path) });
    return response.pipe(catchError((error: HttpErrorResponse) => throwError(() => new Error(
      typeof error.error?.detail === 'string' ? error.error.detail : error.status === 429
        ? 'Muitas perguntas em pouco tempo. Aguarde um minuto e tente novamente.'
        : 'Não foi possível concluir a operação. Confira sua conexão e tente novamente.'
    ))));
  }
}
