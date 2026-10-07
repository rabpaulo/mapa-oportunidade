import { Component, input } from '@angular/core';
import { LucideDynamicIcon, provideLucideIcons, LucideArrowUpRight, LucideArrowUp, LucideChevronLeft, LucideChevronRight,
  LucideDatabase, LucideLayers, LucideMap, LucideMessageSquare, LucideMoon, LucideSun, LucideUsers, LucideSearch,
  LucideSlidersHorizontal, LucideX, LucideMail, LucidePhone, LucideRotateCcw, LucideLocateFixed, LucideDownload } from '@lucide/angular';

@Component({
  selector: 'ceara-icon',
  imports: [LucideDynamicIcon],
  providers: [provideLucideIcons(LucideArrowUpRight, LucideArrowUp, LucideChevronLeft, LucideChevronRight,
    LucideDatabase, LucideLayers, LucideMap, LucideMessageSquare, LucideMoon, LucideSun, LucideUsers, LucideSearch,
    LucideSlidersHorizontal, LucideX, LucideMail, LucidePhone, LucideRotateCcw, LucideLocateFixed, LucideDownload)],
  template: '<svg [lucideIcon]="name()" [size]="size()" aria-hidden="true"></svg>',
  styles: ':host { display: inline-flex; align-items: center; flex-shrink: 0; }'
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input(16);
}
