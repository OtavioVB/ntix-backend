-- Cidade normalizada do evento, para o filtro exato da vitrine (REQ11) sem diferenciar maiúsculas e acentos.
-- Coluna gerada pelo banco: a regra fica num só lugar e vale para as linhas que já existiam.
-- Idempotente: pode ser executado mais de uma vez.

ALTER TABLE events.event
    ADD COLUMN IF NOT EXISTS city_normalized VARCHAR(80)
    GENERATED ALWAYS AS (translate(lower(city), 'áàâãäéèêëíìîïóòôõöúùûüçñ', 'aaaaaeeeeiiiiooooouuuucn')) STORED;
