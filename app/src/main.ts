import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { AppComponent } from './app/app.component';

bootstrapApplication(AppComponent, { providers: [provideHttpClient()] }).catch(() => {
  document.body.textContent = 'Não foi possível abrir a aplicação. Recarregue a página.';
});
