// Retired OMDb-only repair script. Do not silently execute a broader data migration.
console.error('This old director repair is retired. Use node scripts/migrate-movies-tmdb.mjs to prepare a TMDB metadata update, then apply its generated plan.');
process.exitCode=1;
