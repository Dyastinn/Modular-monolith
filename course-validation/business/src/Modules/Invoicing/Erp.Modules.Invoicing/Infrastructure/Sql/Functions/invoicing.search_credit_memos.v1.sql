-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_refund_pending: only memos with a refund still to pay. p_sort: one of the validator's names; NULL = newest first.
CREATE OR REPLACE FUNCTION invoicing.search_credit_memos(p_customer_id bigint, p_search text, p_refund_pending boolean,
    p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, number text, invoice_id bigint, invoice_number text, customer_id bigint, customer_name text, issue_date date,
               reason text, total numeric, applied_to_invoice numeric, refund_due numeric, refunded_on date, return_to_stock boolean)
LANGUAGE sql STABLE
AS $$
    WITH m AS (
        SELECT m.id, m.number::text AS number, m.invoice_id, m.invoice_number::text AS invoice_number, m.customer_id,
               m.customer_name::text AS customer_name, m.issue_date, m.reason::text AS reason, m.total, m.applied_to_invoice,
               m.refund_due, m.refunded_on, m.return_to_stock
        FROM invoicing.credit_memo m
        WHERE (p_customer_id IS NULL OR m.customer_id = p_customer_id)
          AND (p_search IS NULL OR m.number ILIKE '%' || p_search || '%' OR m.invoice_number ILIKE '%' || p_search || '%'
                                OR m.customer_name ILIKE '%' || p_search || '%')
          AND (NOT p_refund_pending OR (m.refund_due > 0 AND m.refunded_on IS NULL))
    )
    SELECT * FROM m
    ORDER BY
        CASE WHEN p_sort = 'number' AND NOT p_desc THEN m.number END ASC,
        CASE WHEN p_sort = 'number' AND p_desc     THEN m.number END DESC,
        CASE WHEN p_sort = 'customerName' AND NOT p_desc THEN m.customer_name END ASC,
        CASE WHEN p_sort = 'customerName' AND p_desc     THEN m.customer_name END DESC,
        CASE WHEN p_sort = 'total' AND NOT p_desc THEN m.total END ASC,
        CASE WHEN p_sort = 'total' AND p_desc     THEN m.total END DESC,
        CASE WHEN p_sort = 'refundDue' AND NOT p_desc THEN m.refund_due END ASC,
        CASE WHEN p_sort = 'refundDue' AND p_desc     THEN m.refund_due END DESC,
        CASE WHEN p_sort = 'issueDate' AND NOT p_desc THEN m.issue_date END ASC,
        m.issue_date DESC, m.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION invoicing.count_credit_memos(p_customer_id bigint, p_search text, p_refund_pending boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM invoicing.credit_memo m
    WHERE (p_customer_id IS NULL OR m.customer_id = p_customer_id)
      AND (p_search IS NULL OR m.number ILIKE '%' || p_search || '%' OR m.invoice_number ILIKE '%' || p_search || '%'
                            OR m.customer_name ILIKE '%' || p_search || '%')
      AND (NOT p_refund_pending OR (m.refund_due > 0 AND m.refunded_on IS NULL));
$$;

CREATE OR REPLACE FUNCTION invoicing.get_credit_memo(p_id bigint)
RETURNS TABLE (id bigint, number text, invoice_id bigint, invoice_number text, customer_id bigint, customer_name text, customer_tin text,
               customer_address text, issue_date date, reason text, return_to_stock boolean, tax_regime integer, vat_rate numeric,
               vatable_sales numeric, vat_amount numeric, zero_rated_sales numeric, vat_exempt_sales numeric,
               special_discount_vat_exemption numeric, special_discount_amount numeric, total numeric, applied_to_invoice numeric,
               refund_due numeric, refunded_on date, refund_method integer, refund_reference text, created_by bigint,
               created_utc timestamptz, print_count integer, seller_name text, seller_trade_name text, seller_tin text,
               seller_address text, seller_invoice_title text, seller_permit_number text, seller_series_range text,
               seller_permit_date_issued text)
LANGUAGE sql STABLE
AS $$
    SELECT m.id, m.number::text, m.invoice_id, m.invoice_number::text, m.customer_id, m.customer_name::text, m.customer_tin::text,
           m.customer_address::text, m.issue_date, m.reason::text, m.return_to_stock, m.tax_regime, m.vat_rate,
           m.vatable_sales, m.vat_amount, m.zero_rated_sales, m.vat_exempt_sales, m.special_discount_vat_exemption,
           m.special_discount_amount, m.total, m.applied_to_invoice, m.refund_due, m.refunded_on, m.refund_method,
           m.refund_reference::text, m.created_by, m.created_utc, m.print_count, m.seller_name::text, m.seller_trade_name::text,
           m.seller_tin::text, m.seller_address::text, m.seller_invoice_title::text, m.seller_permit_number::text,
           m.seller_series_range::text, m.seller_permit_date_issued::text
    FROM invoicing.credit_memo m
    WHERE m.id = p_id;
$$;

CREATE OR REPLACE FUNCTION invoicing.get_credit_memo_lines(p_id bigint)
RETURNS TABLE (description text, unit text, quantity numeric, unit_price numeric, tax_type integer)
LANGUAGE sql STABLE
AS $$
    SELECT l.description::text, l.unit::text, l.quantity, l.unit_price, l.tax_type
    FROM invoicing.credit_memo_line l WHERE l.credit_memo_id = p_id ORDER BY l.id;
$$;
