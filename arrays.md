## Arrays

Arrays are ordered, dynamic lists. You can use both **operators** and **intrinsics**.

### Intrinsics (methods)

- `arr.len()` → number of items  
- `arr.push(value)` → appends; returns new length  
- `arr.pop()` → removes & returns last element or `null` if empty  
- `arr.insert_at(index, value)` → inserts at index  
- `arr.remove_range(start, end)` → remove half-open range `[start, end)`  
- `arr.replace_range(start, end, valueOrArray)` → replace range with a value or list  
- `arr.slice(start?, end?)` → returns a new subarray (half-open)

### Examples

```cfs
var nums = [10, 20, 30];
print(nums.len());      # 3
nums.push(40);          # [10,20,30,40]
print(nums.pop());      # 40, nums -> [10,20,30]
nums.insert_at(1, 99);  # [10,99,20,30]
print(nums.slice(1, 3));# [99,20]
nums.remove_range(0, 2);# [20,30]
nums.replace_range(1, 2, [7,8,9]); # [20,7,8,9]
```

You can also push with the **append operator**:

```cfs
var a = [];
a[] = 1; a[] = 2; a[] = 3;  # same as push
```

---
---
[← Back to README](./README.md)
