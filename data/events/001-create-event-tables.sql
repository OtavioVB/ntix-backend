-- Schema e tabela dos eventos do produtor (REQ9).
-- Idempotente: pode ser executado mais de uma vez.

CREATE SCHEMA IF NOT EXISTS events;

-- Sem chave estrangeira para producers.producer: os scripts são aplicados em ordem de caminho, e data/events
-- vem antes de data/producers. O id_producer só é gravado pela API, a partir do produtor lido pelo slug da rota.
CREATE TABLE IF NOT EXISTS events.event (
    id_event               UUID          NOT NULL CONSTRAINT pk_event PRIMARY KEY,
    id_producer            UUID          NOT NULL,
    title                  VARCHAR(120)  NOT NULL,
    slug                   VARCHAR(126)  NOT NULL,
    description            VARCHAR(5000) NULL,
    category               VARCHAR(32)   NOT NULL CONSTRAINT ck_event_category CHECK (category IN ('SHOWS', 'FESTIVALS', 'THEATER', 'STAND_UP', 'ELECTRONIC', 'SPORTS', 'GASTRONOMY', 'KIDS', 'OTHER')),
    starts_at              TIMESTAMPTZ   NOT NULL,
    ends_at                TIMESTAMPTZ   NOT NULL,
    doors_open_at          TIMESTAMPTZ   NULL,
    venue_name             VARCHAR(120)  NOT NULL,
    address                VARCHAR(256)  NOT NULL,
    city                   VARCHAR(80)   NOT NULL,
    state                  CHAR(2)       NOT NULL CONSTRAINT ck_event_state CHECK (state IN ('AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA', 'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO')),
    age_rating             VARCHAR(32)   NOT NULL CONSTRAINT ck_event_age_rating CHECK (age_rating IN ('FREE', 'AGE_10', 'AGE_12', 'AGE_14', 'AGE_16', 'AGE_18')),
    image_url              VARCHAR(2048) NULL,
    status                 VARCHAR(32)   NOT NULL CONSTRAINT ck_event_status CHECK (status IN ('DRAFT', 'PUBLISHED')),
    created_at             TIMESTAMPTZ   NOT NULL,
    created_correlation_id UUID          NOT NULL,
    updated_at             TIMESTAMPTZ   NOT NULL,
    updated_correlation_id UUID          NOT NULL,
    CONSTRAINT ck_event_period CHECK (ends_at > starts_at)
);
