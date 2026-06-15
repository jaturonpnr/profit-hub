import { Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** Sets the browser tab title from each route's `title`, suffixed with the brand. */
@Injectable()
export class PhTitleStrategy extends TitleStrategy {
  constructor(private readonly title: Title) { super(); }

  override updateTitle(snapshot: RouterStateSnapshot) {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · Profit Hub` : 'Profit Hub');
  }
}
