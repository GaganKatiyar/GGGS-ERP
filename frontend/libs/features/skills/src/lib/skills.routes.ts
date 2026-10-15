import type { Routes } from '@angular/router';
import { MySkillsPage } from './my-skills-page';
import { SkillsPage } from './skills-page';

/** Lazily loaded by the shell at `/skills`. */
export const SKILLS_ROUTES: Routes = [{ path: '', component: SkillsPage, title: 'Skills' }];

/** Lazily loaded by the shell at `/me/skills`. */
export const MY_SKILLS_ROUTES: Routes = [{ path: '', component: MySkillsPage, title: 'My skills' }];
