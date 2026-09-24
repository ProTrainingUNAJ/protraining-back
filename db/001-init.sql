CREATE TABLE IF NOT EXISTS athletes (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    full_name VARCHAR(150) NOT NULL,
    category VARCHAR(100) NOT NULL,
    record_1rm NUMERIC(5,2) NOT NULL DEFAULT 0.00,
    active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Datos iniciales de demostración
INSERT INTO athletes (full_name, category, record_1rm, active)
VALUES 
    ('Lucas Fernández', 'Juveniles M19', 60.00, true),
    ('Mateo Rossi', 'Primera División', 90.00, true),
    ('Santiago Gómez', 'Primera División', 105.00, true)
ON CONFLICT DO NOTHING;
