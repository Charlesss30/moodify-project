BEGIN;
ALTER TABLE nhac ADD COLUMN IF NOT EXISTS source varchar(20);
ALTER TABLE nhac ADD COLUMN IF NOT EXISTS externalid varchar(64);
ALTER TABLE nhac ADD COLUMN IF NOT EXISTS sourceurl text;
ALTER TABLE nhac ADD COLUMN IF NOT EXISTS releasedate varchar(40);
ALTER TABLE nhac ADD COLUMN IF NOT EXISTS lastsyncedat timestamptz;
ALTER TABLE nhac ADD COLUMN IF NOT EXISTS moodsource varchar(40);
CREATE UNIQUE INDEX IF NOT EXISTS ix_nhac_source_externalid ON nhac (source, externalid);
COMMIT;
