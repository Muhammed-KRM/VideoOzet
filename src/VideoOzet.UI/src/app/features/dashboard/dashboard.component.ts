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

  showEditModal = false;
  editingCourse = { id: '', ad: '', aciklama: '' };
  isUpdating = false;

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

  openEditCourse(egitim: any, event: Event) {
    event.stopPropagation();
    this.editingCourse = {
      id: egitim.id,
      ad: egitim.ad,
      aciklama: egitim.aciklama || ''
    };
    this.showEditModal = true;
  }

  updateCourse() {
    if (!this.editingCourse.ad) return;
    this.isUpdating = true;
    this.apiService.updateEgitim(this.editingCourse.id, this.editingCourse).subscribe({
      next: () => {
        this.isUpdating = false;
        this.showEditModal = false;
        this.loadEgitimler();
      },
      error: (err) => {
        console.error('Güncelleme hatası', err);
        alert('Eğitim güncellenirken bir hata oluştu.');
        this.isUpdating = false;
      }
    });
  }

  deleteCourse(egitim: any, event: Event) {
    event.stopPropagation();
    if (!confirm(`"${egitim.ad}" adlı eğitimi silmek istediğinize emin misiniz? Eğitim içindeki tüm video ve dökümanlar da silinecektir.`)) {
      return;
    }

    this.apiService.deleteEgitim(egitim.id).subscribe({
      next: () => {
        this.loadEgitimler();
      },
      error: (err) => {
        console.error('Silme hatası', err);
        alert('Eğitim silinirken bir hata oluştu.');
      }
    });
  }
}
