-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Per customer and quarter: the tax they withheld from their payments, what their BIR Forms 2307 certify, and the gap.
CREATE OR REPLACE FUNCTION invoicing.withholding_summary(p_year integer)
RETURNS TABLE (customer_id bigint, customer_name text, quarter integer, withheld numeric, certified numeric, missing numeric)
LANGUAGE sql STABLE
AS $$
    WITH withheld AS (
        SELECT i.customer_id, MAX(i.customer_name) AS customer_name, EXTRACT(QUARTER FROM p.paid_on)::integer AS quarter,
               SUM(p.tax_withheld) AS amount
        FROM invoicing.payment p
        JOIN invoicing.invoice i ON i.id = p.invoice_id
        WHERE p.voided_utc IS NULL AND p.tax_withheld > 0 AND EXTRACT(YEAR FROM p.paid_on) = p_year
        GROUP BY i.customer_id, EXTRACT(QUARTER FROM p.paid_on)
    ),
    certified AS (
        SELECT c.customer_id, MAX(c.customer_name) AS customer_name, c.quarter, SUM(c.amount) AS amount
        FROM invoicing.withholding_certificate c
        WHERE c.voided_utc IS NULL AND c.year = p_year
        GROUP BY c.customer_id, c.quarter
    )
    SELECT COALESCE(w.customer_id, c.customer_id), COALESCE(w.customer_name, c.customer_name)::text,
           COALESCE(w.quarter, c.quarter), COALESCE(w.amount, 0), COALESCE(c.amount, 0),
           GREATEST(COALESCE(w.amount, 0) - COALESCE(c.amount, 0), 0)
    FROM withheld w
    FULL JOIN certified c ON c.customer_id = w.customer_id AND c.quarter = w.quarter
    ORDER BY 2, 3;
$$;

CREATE OR REPLACE FUNCTION invoicing.list_withholding_certificates(p_year integer)
RETURNS TABLE (id bigint, customer_id bigint, customer_name text, quarter integer, amount numeric, reference text,
               received_on date, recorded_by bigint, recorded_utc timestamptz, voided_utc timestamptz, void_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT c.id, c.customer_id, c.customer_name::text, c.quarter, c.amount, c.reference::text, c.received_on, c.recorded_by,
           c.recorded_utc, c.voided_utc, c.void_reason::text
    FROM invoicing.withholding_certificate c
    WHERE c.year = p_year
    ORDER BY c.customer_name, c.quarter, c.id;
$$;
