BEGIN;
ALTER TABLE phim ALTER COLUMN diemdanhgiatb TYPE decimal(4,2);
INSERT INTO tam_trang (tentamtrang, minvalence, maxvalence, minarousal, maxarousal)
VALUES
('Vui vẻ', 0.5, 1.0, 0.4, 0.9),
('Buồn bã', -1.0, -0.2, -0.9, 0.1),
('Thư giãn', 0.2, 0.8, -0.9, -0.1),
('Hào hứng', 0.4, 0.9, 0.6, 1.0),
('Tập trung', 0.0, 0.5, -0.6, 0.2),
('Lãng mạn', 0.3, 0.8, -0.3, 0.4),
('Hoài niệm', -0.3, 0.4, -0.5, 0.1)
ON CONFLICT (tentamtrang) DO NOTHING;
COMMIT;
