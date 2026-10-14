import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { type RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** The product name, as the browser tab and the sign-in page show it. */
export const PRODUCT_NAME = 'PeopleOS';

/**
 * Browser tab titles as "Page · PeopleOS", so a user with several tabs open can tell them apart
 * and still see which product each one is. A route without a title shows the product name alone.
 */
@Injectable({ providedIn: 'root' })
export class ProductTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · ${PRODUCT_NAME}` : PRODUCT_NAME);
  }
}
