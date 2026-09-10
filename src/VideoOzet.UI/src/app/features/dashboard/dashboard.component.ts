import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit {
  egitimler: any[] = [];
  isLoading = true;
  showModal = false;
  
  newCourse = { ad: '', aciklama: '' };
  isCreating = false;

  private apiService = inject(ApiService);
  private router = inject(Router);

  ngOnInit() {
    this.loadEgitimler();
  }

  loadEgitimler() {
    this.isLoading = true;
    this.apiService.getEgitimler().subscribe({
      next: (data) => {
        this.egitimler = data;
        this.isLoading = false;
      },
      error: (err) => {
        console.error('Eğitimler yüklenemedi', err);
        this.isLoading = false;
      }
    });
  }

  openCourse(id: string) {
    this.router.navigate(['/egitim', id]);
  }

  createCourse() {
    if (!this.newCourse.ad) return;
    this.isCreating = true;
    this.apiService.createEgitim(this.newCourse).subscribe({
      next: () => {
        this.isCreating = false;
        this.showModal = false;
        this.newCourse = { ad: '', aciklama: '' };
        this.loadEgitimler();
      },
      error: (err) => {
        console.error('Oluşturma hatası', err);
        this.isCreating = false;
      }
    });
  }
}
