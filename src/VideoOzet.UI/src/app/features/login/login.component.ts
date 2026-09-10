import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html'
})
export class LoginComponent {
  apiKey = '';
  errorMessage = '';
  isLoading = false;

  private apiService = inject(ApiService);
  private router = inject(Router);

  login() {
    if (!this.apiKey) {
      this.errorMessage = 'Lütfen API anahtarını girin.';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    
    // Geçici olarak local storage'a kaydet ki interceptor kullansın
    localStorage.setItem('admin_token', this.apiKey);

    this.apiService.validateApiKey().subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/dashboard']);
      },
      error: () => {
        this.isLoading = false;
        this.errorMessage = 'Geçersiz API Anahtarı.';
        localStorage.removeItem('admin_token');
      }
    });
  }
}
