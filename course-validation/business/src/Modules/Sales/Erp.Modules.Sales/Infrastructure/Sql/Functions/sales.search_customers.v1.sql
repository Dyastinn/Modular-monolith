-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_customer_id set: that one customer. Pending = submitted orders still waiting for a manager.
CREATE OR REPLACE FUNCTION sales.search_customers(p_customer_id bigint, p_search text, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, tin text, address text, payment_terms_days integer, is_blocked boolean,
               credit_limit numeric, outstanding_balance numeric, pending_total numeric, available_credit numeric)
LANGUAGE sql STABLE
AS $$
    WITH pending AS (
        SELECT o.customer_id,
               SUM(COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_price, 2))
                             FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0)
                   - o.special_discount_vat_exemption - o.special_discount_amount) AS total
        FROM sales.sales_order o
        WHERE o.status = 1
        GROUP BY o.customer_id
    )
    SELECT c.id, c.name::text, c.tin::text, c.address::text, c.payment_terms_days, c.is_blocked,
           c.credit_limit, c.outstanding_balance, COALESCE(p.total, 0),
           GREATEST(c.credit_limit - c.outstanding_balance - COALESCE(p.total, 0), 0)
    FROM sales.customer c
    LEFT JOIN pending p ON p.customer_id = c.id
    WHERE (p_customer_id IS NULL OR c.id = p_customer_id)
      AND (p_search IS NULL OR c.name ILIKE '%' || p_search || '%' OR c.tin ILIKE '%' || p_search || '%')
    ORDER BY c.name, c.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION sales.count_customers(p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM sales.customer c
    WHERE p_search IS NULL OR c.name ILIKE '%' || p_search || '%' OR c.tin ILIKE '%' || p_search || '%';
$$;
