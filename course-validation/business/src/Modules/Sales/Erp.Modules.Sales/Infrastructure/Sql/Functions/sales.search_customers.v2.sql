-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: the customer's own approval limit (NULL = the company's), and sorting (NULL = by name).
DROP FUNCTION IF EXISTS sales.search_customers(bigint, text, integer, integer);

CREATE OR REPLACE FUNCTION sales.search_customers(p_customer_id bigint, p_search text, p_sort text, p_desc boolean,
    p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, tin text, address text, payment_terms_days integer, is_blocked boolean,
               credit_limit numeric, outstanding_balance numeric, pending_total numeric, available_credit numeric,
               approval_threshold numeric)
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
    ), c AS (
        SELECT c.id, c.name::text AS name, c.tin::text AS tin, c.address::text AS address, c.payment_terms_days, c.is_blocked,
               c.credit_limit, c.outstanding_balance, COALESCE(p.total, 0) AS pending_total,
               GREATEST(c.credit_limit - c.outstanding_balance - COALESCE(p.total, 0), 0) AS available_credit,
               c.approval_threshold
        FROM sales.customer c
        LEFT JOIN pending p ON p.customer_id = c.id
        WHERE (p_customer_id IS NULL OR c.id = p_customer_id)
          AND (p_search IS NULL OR c.name ILIKE '%' || p_search || '%' OR c.tin ILIKE '%' || p_search || '%')
    )
    SELECT * FROM c
    ORDER BY
        CASE WHEN p_sort = 'creditLimit'        AND NOT p_desc THEN c.credit_limit        END ASC,
        CASE WHEN p_sort = 'creditLimit'        AND p_desc     THEN c.credit_limit        END DESC,
        CASE WHEN p_sort = 'outstandingBalance' AND NOT p_desc THEN c.outstanding_balance END ASC,
        CASE WHEN p_sort = 'outstandingBalance' AND p_desc     THEN c.outstanding_balance END DESC,
        CASE WHEN p_sort = 'pendingTotal'       AND NOT p_desc THEN c.pending_total       END ASC,
        CASE WHEN p_sort = 'pendingTotal'       AND p_desc     THEN c.pending_total       END DESC,
        CASE WHEN p_sort = 'availableCredit'    AND NOT p_desc THEN c.available_credit    END ASC,
        CASE WHEN p_sort = 'availableCredit'    AND p_desc     THEN c.available_credit    END DESC,
        CASE WHEN p_sort = 'paymentTermsDays'   AND NOT p_desc THEN c.payment_terms_days  END ASC,
        CASE WHEN p_sort = 'paymentTermsDays'   AND p_desc     THEN c.payment_terms_days  END DESC,
        CASE WHEN p_sort = 'name'               AND p_desc     THEN c.name                END DESC,
        c.name, c.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
