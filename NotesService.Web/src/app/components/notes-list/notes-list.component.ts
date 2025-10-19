// src/app/components/notes-list/notes-list.component.ts
import { Component, OnInit } from '@angular/core';
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
export class NotesListComponent implements OnInit {
  notes: Note[] = [];
  filteredNotes: Note[] = [];
  searchTerm: string = '';
  loading: boolean = false;
  error: string | null = null;

  // Form fields
  showForm: boolean = false;
  editingNote: Note | null = null;
  noteContent: string = '';

  constructor(private notesService: NotesService) { }

  ngOnInit(): void {
    this.loadNotes();
  }

  loadNotes(): void {
    this.loading = true;
    this.error = null;

    this.notesService.getAllNotes().subscribe({
      next: (data) => {
        this.notes = data;
        this.filteredNotes = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = 'Failed to load notes. Please try again.';
        this.loading = false;
        console.error('Error loading notes:', err);
      }
    });
  }

  filterNotes(): void {
    if (!this.searchTerm) {
      this.filteredNotes = this.notes;
      return;
    }

    const term = this.searchTerm.toLowerCase();
    this.filteredNotes = this.notes.filter(note =>
      note.content.toLowerCase().includes(term) ||
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
      alert('Please enter note content');
      return;
    }

    this.loading = true;

    if (this.editingNote && this.editingNote.id) {
      // Update existing note
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
      // Create new note
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

  deleteNote(id: number | undefined): void {
    if (!id) return;

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
    const date = new Date(dateString);
    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
  }
}
