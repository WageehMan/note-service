// src/app/services/notes.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, retry, map } from 'rxjs/operators';

export interface Note {
  id?: string;  // Changed to string (UUID)
  content: string;
  summary?: string | null;
  createdAt?: string;
  updatedAt?: string;
}

// API response format
interface ApiNote {
  id: string;
  content: string;
  summary?: string | null;
  created_at: string;
  updated_at: string;
}

interface ApiResponse {
  count: number;
  notes: ApiNote[];
}

export interface CreateNoteRequest {
  content: string;
}

export interface UpdateNoteRequest {
  content: string;
}

@Injectable({
  providedIn: 'root'
})
export class NotesService {
  private apiUrl = 'https://u1t4q70g86.execute-api.us-east-1.amazonaws.com/prod/notes';

  constructor(private http: HttpClient) { }

  // Convert API note to frontend note (snake_case to camelCase)
  private mapNote(apiNote: ApiNote): Note {
    return {
      id: apiNote.id,
      content: apiNote.content,
      summary: apiNote.summary,
      createdAt: apiNote.created_at,
      updatedAt: apiNote.updated_at
    };
  }

  getAllNotes(): Observable<Note[]> {
    return this.http.get<ApiResponse>(this.apiUrl).pipe(
      map(response => {
        console.log('Raw API response:', response);

        // Extract notes array from response
        if (response && Array.isArray(response.notes)) {
          return response.notes.map(note => this.mapNote(note));
        }

        // Fallback: if response is already an array
        if (Array.isArray(response)) {
          return (response as any[]).map(note => this.mapNote(note));
        }

        console.error('Unexpected API response format:', response);
        return [];
      }),
      retry(2),
      catchError(this.handleError)
    );
  }

  getNoteById(id: string): Observable<Note> {
    return this.http.get<ApiNote>(`${this.apiUrl}/${id}`).pipe(
      map(note => this.mapNote(note)),
      catchError(this.handleError)
    );
  }

  createNote(note: CreateNoteRequest): Observable<Note> {
    return this.http.post<ApiNote>(this.apiUrl, note).pipe(
      map(note => this.mapNote(note)),
      catchError(this.handleError)
    );
  }

  updateNote(id: string, note: UpdateNoteRequest): Observable<Note> {
    return this.http.put<ApiNote>(`${this.apiUrl}/${id}`, note).pipe(
      map(note => this.mapNote(note)),
      catchError(this.handleError)
    );
  }

  deleteNote(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(
      catchError(this.handleError)
    );
  }

  private handleError(error: any): Observable<never> {
    let errorMessage = 'An unknown error occurred';

    if (error.error instanceof ErrorEvent) {
      errorMessage = `Error: ${error.error.message}`;
    } else {
      errorMessage = `Error Code: ${error.status}\nMessage: ${error.message}`;
    }

    console.error('API Error:', errorMessage);
    return throwError(() => new Error(errorMessage));
  }
}
