import { Routes } from '@angular/router';
import { LoginComponent } from './features/login/login.component';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { CourseDetailComponent } from './features/course-detail/course-detail.component';
import { SeriesPlannerComponent } from './features/series-planner/series-planner.component';
import { EpisodeViewerComponent } from './features/episode-viewer/episode-viewer.component';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'egitim/:id', component: CourseDetailComponent, canActivate: [authGuard] },
  { path: 'series-planner/:id', component: SeriesPlannerComponent, canActivate: [authGuard] },
  { path: 'episode-viewer/:id/:planNo/:bolumNo', component: EpisodeViewerComponent, canActivate: [authGuard] },
  { path: '**', redirectTo: 'dashboard' }
];
