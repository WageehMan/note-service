// src/app/components/notes-list/notes-list.component.ts
import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NotesService, Note } from '../../services/notes.service';

@Component({
  selector: 'app-notes-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './notes-list.component.html',
  styleUrls: ['./notes-list.component.css']
})
export class NotesListComponent implements OnInit, OnDestroy {
  notes: Note[] = [];
  filteredNotes: Note[] = [];
  searchTerm: string = '';
  loading: boolean = false;
  error: string | null = null;

  showForm: boolean = false;
  editingNote: Note | null = null;
  noteContent: string = '';

  private refreshInterval: any;

  constructor(private notesService: NotesService) { }

  ngOnInit(): void {
    this.loadNotes();
    this.refreshInterval = setInterval(() => this.loadNotes(), 30000);
  }

  ngOnDestroy(): void {
    if (this.refreshInterval) {
      clearInterval(this.refreshInterval);
    }
  }

  loadNotes(): void {
    this.loading = true;
    this.error = null;

    this.notesService.getAllNotes().subscribe({
      next: (data) => {
        console.log('Processed notes:', data);

        if (Array.isArray(data)) {
          this.notes = data;
          this.filterNotes();
        } else {
          console.error('Expected array but got:', data);
          this.notes = [];
          this.filteredNotes = [];
          this.error = 'Unexpected data format from server';
        }

        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load notes. Please try again.';
        this.loading = false;
        this.notes = [];
        this.filteredNotes = [];
        console.error('Error loading notes:', err);
      }
    });
  }

  filterNotes(): void {
    if (!Array.isArray(this.notes)) {
      this.filteredNotes = [];
      return;
    }

    if (!this.searchTerm) {
      this.filteredNotes = [...this.notes];
      return;
    }

    const term = this.searchTerm.toLowerCase();
    this.filteredNotes = this.notes.filter(note =>
      note.content?.toLowerCase().includes(term) ||
      (note.summary && note.summary.toLowerCase().includes(term))
    );
  }

  openCreateForm(): void {
    this.showForm = true;
    this.editingNote = null;
    this.noteContent = '';
  }

  openEditForm(note: Note): void {
    this.showForm = true;
    this.editingNote = note;
    this.noteContent = note.content;
  }

  closeForm(): void {
    this.showForm = false;
    this.editingNote = null;
    this.noteContent = '';
  }

  saveNote(): void {
    if (!this.noteContent.trim()) {
      this.error = 'Please enter note content';
      return;
    }

    this.loading = true;

    if (this.editingNote && this.editingNote.id) {
      this.notesService.updateNote(this.editingNote.id, { content: this.noteContent }).subscribe({
        next: () => {
          this.loadNotes();
          this.closeForm();
        },
        error: (err) => {
          this.error = 'Failed to update note';
          this.loading = false;
          console.error('Error updating note:', err);
        }
      });
    } else {
      this.notesService.createNote({ content: this.noteContent }).subscribe({
        next: () => {
          this.loadNotes();
          this.closeForm();
        },
        error: (err) => {
          this.error = 'Failed to create note';
          this.loading = false;
          console.error('Error creating note:', err);
        }
      });
    }
  }

  deleteNote(id: string | undefined): void {
    if (!id) {
      this.error = 'Cannot delete note: Invalid ID';
      return;
    }

    if (!confirm('Are you sure you want to delete this note?')) {
      return;
    }

    this.loading = true;

    this.notesService.deleteNote(id).subscribe({
      next: () => {
        this.loadNotes();
      },
      error: (err) => {
        this.error = 'Failed to delete note';
        this.loading = false;
        console.error('Error deleting note:', err);
      }
    });
  }

  formatDate(dateString: string | undefined): string {
    if (!dateString) return 'N/A';

    // Handle default .NET dates
    if (dateString.startsWith('0001-01-01')) {
      return 'N/A';
    }

    const date = new Date(dateString);

    if (isNaN(date.getTime())) {
      return 'N/A';
    }

    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
  }

  formatId(id: string | undefined): string {
    if (!id) return 'N/A';
    // Show first 8 characters of UUID
    return id.substring(0, 8);
  }

  isSummarizing(note: Note): boolean {
    if (note.summary && note.summary.trim().length > 0) {
      return false;
    }

    if (!note.createdAt || note.createdAt.startsWith('0001-01-01')) {
      return true; // Assume new note is being summarized
    }

    const createdAt = new Date(note.createdAt);
    if (isNaN(createdAt.getTime())) {
      return true;
    }

    const now = new Date();
    const diffMinutes = (now.getTime() - createdAt.getTime()) / 1000 / 60;

    return diffMinutes < 5;
  }
}
