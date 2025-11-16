## While Loops


### while loop

```cfs
var counter = 1;
while (counter <= 5) {
    print(counter);
    counter++;
}
```

### do-while loop

```
var abc = 0;
do
{
    abc++;
    if (abc % 2 == 0) continue;
    print(abc);
}
while (abc < 10);
```
> Note: The semicolon after the do-while expression is optional. I only added support for it for convenience.


---
[← Back to README](./README.md)
