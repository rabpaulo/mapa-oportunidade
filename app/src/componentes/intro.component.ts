import { Component, OnDestroy, output, signal } from '@angular/core';
import { IconComponent } from './icon.component';
import type { Screen } from '../lib/store';

@Component({
  selector: 'ceara-intro',
  imports: [IconComponent],
  templateUrl: './intro.component.html',
  styleUrl: './intro.component.css'
})
export class IntroComponent implements OnDestroy {
  readonly open = output<Screen>();
  readonly finished = output<void>();
  readonly leaving = signal(false);
  private exitTimer?: ReturnType<typeof setTimeout>;

  enter(screen: Screen) {
    if (this.leaving()) return;
    this.leaving.set(true);
    this.open.emit(screen);
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    this.exitTimer = setTimeout(() => this.finished.emit(), reducedMotion ? 0 : 760);
  }

  ngOnDestroy() { clearTimeout(this.exitTimer); }
}
