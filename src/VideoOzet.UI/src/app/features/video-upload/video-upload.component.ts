import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-video-upload',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './video-upload.component.html'
})
export class VideoUploadComponent {
  @Input() egitimId!: string;
  isDragging = false;
  isUploading = false;
  
  private apiService = inject(ApiService);

  onDragOver(event: DragEvent) {
    event.preventDefault();
    this.isDragging = true;
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    this.isDragging = false;
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.isDragging = false;
    if (event.dataTransfer?.files.length) {
      this.handleFiles(event.dataTransfer.files);
    }
  }

  onFileSelected(event: any) {
    if (event.target.files.length) {
      this.handleFiles(event.target.files);
    }
  }

  private handleFiles(files: FileList) {
    if (files.length === 0) return;
    
    const file = files[0];
    
    if (!file.type.startsWith('video/')) {
      alert('Lütfen sadece video dosyası seçin.');
      return;
    }

    const formData = new FormData();
    formData.append('file', file);
    formData.append('baslik', file.name);

    this.isUploading = true;
    this.apiService.uploadVideo(this.egitimId, formData).subscribe({
      next: () => {
        this.isUploading = false;
        // SignalR will handle the progress UI from here
      },
      error: (err) => {
        console.error('Yükleme hatası', err);
        this.isUploading = false;
      }
    });
  }
}
