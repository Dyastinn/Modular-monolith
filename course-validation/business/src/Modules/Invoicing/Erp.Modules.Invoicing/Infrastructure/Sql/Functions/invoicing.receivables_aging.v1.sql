-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- What each customer owed on p_as_of, by how late it was. Payments made after that day don't count yet.
CREATE OR REPLACE FUNCTION invoicing.receivables_aging(p_as_of date)
RETURNS TABLE (customer_id bigint, customer_name text, not_yet_due numeric, days_1_to_30 numeric, days_31_to_60 numeric,
               days_61_to_90 numeric, over_90 numeric, total numeric)
LANGUAGE sql STABLE
AS $$
    WITH open_invoice AS (
        SELECT i.customer_id, i.customer_name, i.due_date,
               i.total - COALESCE((SELECT SUM(p.amount) FROM invoicing.payment p
                                   WHERE p.invoice_id = i.id AND p.voided_utc IS NULL AND p.paid_on <= p_as_of), 0) AS open_amount
        FROM invoicing.invoice i
        WHERE i.status <> 3 AND i.issue_date <= p_as_of
    )
    SELECT o.customer_id, MAX(o.customer_name)::text,
           SUM(CASE WHEN p_as_of <= o.due_date THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date BETWEEN 1 AND 30 THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date BETWEEN 31 AND 60 THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date BETWEEN 61 AND 90 THEN o.open_amount ELSE 0 END),
           SUM(CASE WHEN p_as_of - o.due_date > 90 THEN o.open_amount ELSE 0 END),
           SUM(o.open_amount)
    FROM open_invoice o
    WHERE o.open_amount > 0
    GROUP BY o.customer_id
    ORDER BY MAX(o.customer_name);
$$;
