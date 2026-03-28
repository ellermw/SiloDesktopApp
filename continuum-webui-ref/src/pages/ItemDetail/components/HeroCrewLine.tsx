import { Link } from "react-router";
import type { CrewMember } from "@/api/types";
import { buildPersonCatalogHref } from "@/pages/catalogSearchParams";

interface HeroCrewLineProps {
  crew: CrewMember[];
  genres?: string[];
  jobLabel?: string;
}

interface CrewPerson {
  name: string;
  personId: string;
}

function CrewNames({ people }: { people: CrewPerson[] }) {
  return (
    <>
      {people.map((p, i) => (
        <span key={p.name}>
          {i > 0 && ", "}
          {p.personId ? (
            <Link
              to={buildPersonCatalogHref(p.personId)}
              className="font-medium text-foreground/70 transition-colors hover:text-foreground/90"
            >
              {p.name}
            </Link>
          ) : (
            <span className="font-medium text-foreground/70">{p.name}</span>
          )}
        </span>
      ))}
    </>
  );
}

export default function HeroCrewLine({
  crew,
  genres,
  jobLabel = "Directed by",
}: HeroCrewLineProps) {
  const directors = crew
    .filter((c) => c.job === "Director")
    .map((c): CrewPerson => ({ name: c.name, personId: c.person_id }))
    .slice(0, 2);

  const writers = crew
    .filter((c) => c.job === "Writer" || c.job === "Screenplay")
    .map((c): CrewPerson => ({ name: c.name, personId: c.person_id }))
    .slice(0, 2);

  const hasDirectors = directors.length > 0;
  const hasWriters = writers.length > 0;
  const hasGenres = genres && genres.length > 0;

  if (!hasDirectors && !hasWriters && !hasGenres) return null;

  return (
    <div className="text-[13px] text-muted-foreground">
      {hasDirectors && (
        <>
          <span className="text-muted-foreground/60">{jobLabel} </span>
          <CrewNames people={directors} />
        </>
      )}
      {hasDirectors && hasWriters && <span className="mx-2 text-muted-foreground/40">&middot;</span>}
      {hasWriters && (
        <>
          <span className="text-muted-foreground/60">Written by </span>
          <CrewNames people={writers} />
        </>
      )}
      {(hasDirectors || hasWriters) && hasGenres && (
        <span className="mx-2 text-muted-foreground/40">&middot;</span>
      )}
      {hasGenres &&
        genres.map((g, i) => (
          <span key={g}>
            <span className="text-foreground/60">{g}</span>
            {i < genres.length - 1 && <span className="mx-1.5 text-muted-foreground/40">&middot;</span>}
          </span>
        ))}
    </div>
  );
}
