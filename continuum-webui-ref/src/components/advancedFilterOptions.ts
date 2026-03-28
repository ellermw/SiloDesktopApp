export interface AdvancedFilters {
  type: string;
  sort: string;
  order: string;
  genre: string;
  year_min: string;
  year_max: string;
  content_rating: string;
}

export const ADVANCED_FILTER_GENRES = [
  "Action",
  "Adventure",
  "Animation",
  "Comedy",
  "Crime",
  "Documentary",
  "Drama",
  "Family",
  "Fantasy",
  "History",
  "Horror",
  "Music",
  "Mystery",
  "Romance",
  "Science Fiction",
  "Thriller",
  "War",
  "Western",
] as const;

export const ADVANCED_FILTER_CONTENT_RATINGS = [
  "G",
  "PG",
  "PG-13",
  "R",
  "NC-17",
  "TV-Y",
  "TV-G",
  "TV-PG",
  "TV-14",
  "TV-MA",
  "NR",
] as const;

export const DEFAULT_LIBRARY_BROWSE_FILTERS: AdvancedFilters = {
  type: "all",
  sort: "sort_title",
  order: "asc",
  genre: "all",
  year_min: "",
  year_max: "",
  content_rating: "all",
};
