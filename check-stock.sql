select s.*, t.r 
from stock s
join (
    select l.i, l.w, SUM(l.q) as r
    from orders o, 
        lateral unnest(o.item_ids,o.warehouse_ids,o.QUANTITIES) as l(i,w,q)
    group by 1,2) t on (s.item_id,s.warehouse_id) = (t.i,t.w)
where s.reserved <> t.r
