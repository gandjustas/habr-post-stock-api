TRUNCATE TABLE orders CASCADE;
TRUNCATE TABLE stock;
INSERT INTO stock 
select *, random(10000, 100000) as quantity from generate_series(1,10) item_id, generate_series(1,10) warehouse_id;